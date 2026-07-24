using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using IronDev.Core.Models;
using IronDev.Core.Sandbox;
using IronDev.Core.Workbench;
using IronDev.Data.Models;
using IronDev.Infrastructure.Services;
using IronDev.Infrastructure.Services.Sandbox;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api;

[TestClass]
public sealed class WorkbenchBuilderAuthorizationApiTests : ApiTestBase
{
    private const string ProfileDefinitionId =
        RepositorySetupProfileIds.GreenfieldWinFormsNet10MstestV1;

    [TestMethod]
    public async Task ReadyDeliveryProject_CreatesCanonicalCore_InRequestedTicketOrder_AndReplaysExactly()
    {
        using var scenario = new AuthorizationScenario();
        using var factory = scenario.CreateFactory();
        using var client = await AuthenticatedClientAsync(factory);
        var fixture = await CreateReadyDeliveryProjectAsync(
            client,
            "Ordered Builder package",
            scenario.Observer,
            scenario.Branch,
            "Sign in",
            "Show profile");
        var requestedOrder = new[]
        {
            fixture.Tickets[1].Id,
            fixture.Tickets[0].Id
        };
        var operationId = Guid.NewGuid();

        var firstResponse = await client.PostAsJsonAsync(
            WorkPackagesUrl(fixture.ProjectId),
            WorkPackagePayload(fixture, operationId, requestedOrder));
        Assert.AreEqual(
            HttpStatusCode.Created,
            firstResponse.StatusCode,
            await firstResponse.Content.ReadAsStringAsync());
        var first = await ReadAsync<BuilderWorkPackageResult>(firstResponse);

        Assert.IsFalse(first.IsReplay);
        Assert.AreEqual(operationId, first.ClientOperationId);
        Assert.AreEqual(BuilderWorkPackageCoreContract.CanonicalizationVersion1,
            first.Core.CanonicalizationVersion);
        Assert.AreEqual(fixture.ProjectId, first.Core.ProjectId);
        Assert.AreEqual(1, first.Core.TenantId);
        CollectionAssert.AreEqual(
            requestedOrder,
            first.Core.Tickets.Select(static ticket => ticket.TicketId).ToArray());
        CollectionAssert.AreEqual(
            new[] { 1, 2 },
            first.Core.Tickets.Select(static ticket => ticket.Ordinal).ToArray());
        CollectionAssert.AreEqual(
            new[] { 1L, 1L },
            first.Core.Tickets.Select(static ticket => ticket.TicketRevision).ToArray());
        Assert.AreEqual("main", first.Core.BranchName);
        Assert.IsTrue(first.Core.Tickets.All(static ticket =>
            ticket.WorkItemId > 0 &&
            ticket.WorkItemContractId > 0 &&
            ticket.WorkItemContractSha256.Length == 64 &&
            ticket.AcceptanceCriteria.Length > 0 &&
            ticket.PermittedFiles.Count == 2));
        Assert.IsTrue(first.Core.ReadinessAssessment.Id > 0);
        Assert.AreEqual(64, first.Core.ReadinessAssessment.EvidenceSha256.Length);
        Assert.AreEqual(64, first.Core.RepositoryObservation.EvidenceSha256.Length);
        Assert.IsTrue(first.Core.CodeIndex.Sources.Count > 0);
        Assert.AreEqual(BuilderRoleContract.BuilderAgentVersion, first.Core.BuilderAgentVersion);
        Assert.AreEqual(BuilderRoleContract.PromptVersion, first.Core.PromptVersion);
        Assert.AreEqual(BuilderRoleContract.ToolPolicyVersion, first.Core.ToolPolicyVersion);
        Assert.AreEqual(BuilderRoleContract.ContextSchemaVersion, first.Core.ContextSchemaVersion);
        Assert.AreEqual(BuilderRoleContract.OutputSchemaVersion, first.Core.OutputSchemaVersion);
        Assert.IsTrue(first.Core.EffectiveProfile.BuilderConfigurationId != Guid.Empty);
        Assert.IsTrue(first.Core.Sandbox.QualificationAttemptId != Guid.Empty);
        Assert.AreEqual(64, first.Core.Sandbox.QualifiedImageDigest.Length);
        Assert.AreEqual(64, first.BuilderWorkPackageCoreHash.Length);
        Assert.AreEqual(
            first.BuilderWorkPackageCoreHash.ToLowerInvariant(),
            first.BuilderWorkPackageCoreHash);
        Assert.AreEqual(
            BuilderWorkPackageCoreCodec.ComputeHash(first.Core),
            first.BuilderWorkPackageCoreHash);

        var replayResponse = await client.PostAsJsonAsync(
            WorkPackagesUrl(fixture.ProjectId),
            WorkPackagePayload(fixture, operationId, requestedOrder));
        Assert.AreEqual(
            HttpStatusCode.OK,
            replayResponse.StatusCode,
            await replayResponse.Content.ReadAsStringAsync());
        var replay = await ReadAsync<BuilderWorkPackageResult>(replayResponse);
        Assert.IsTrue(replay.IsReplay);
        Assert.AreEqual(first.Core.Id, replay.Core.Id);
        Assert.AreEqual(first.Core.CreatedAtUtc, replay.Core.CreatedAtUtc);
        Assert.AreEqual(first.BuilderWorkPackageCoreHash, replay.BuilderWorkPackageCoreHash);

        var mismatchResponse = await client.PostAsJsonAsync(
            WorkPackagesUrl(fixture.ProjectId),
            WorkPackagePayload(fixture, operationId, requestedOrder.Reverse().ToArray()));
        Assert.AreEqual(
            HttpStatusCode.Conflict,
            mismatchResponse.StatusCode,
            await mismatchResponse.Content.ReadAsStringAsync());
        await AssertErrorAsync(
            mismatchResponse,
            BuilderAuthorizationOperationMismatchException.ErrorCode);

        await AssertPr07AWriteCountsAsync(
            fixture.ProjectId,
            cores: 1,
            ticketReferences: 2,
            authorizations: 0,
            completedOperations: 1);
    }

    [TestMethod]
    public async Task GrantAuthorization_IsExactSingleUse_AuthorizationFreeCoreRemainsUnchanged_AndReplaySurvivesLeaseRenewal()
    {
        using var scenario = new AuthorizationScenario();
        using var factory = scenario.CreateFactory();
        using var client = await AuthenticatedClientAsync(factory);
        var fixture = await CreateReadyDeliveryProjectAsync(
            client,
            "Exact Builder authorization",
            scenario.Observer,
            scenario.Branch,
            "Sign in");
        var package = await CreateWorkPackageAsync(
            client,
            fixture,
            Guid.NewGuid(),
            [fixture.Tickets[0].Id]);
        var canonicalBefore = BuilderWorkPackageCoreCodec.SerializeCanonical(package.Core);
        var hashBefore = BuilderWorkPackageCoreCodec.ComputeHash(package.Core);
        var operationId = Guid.NewGuid();

        var grantResponse = await client.PostAsJsonAsync(
            AuthorizationsUrl(fixture.ProjectId),
            AuthorizationPayload(fixture, operationId, package));
        Assert.AreEqual(
            HttpStatusCode.Created,
            grantResponse.StatusCode,
            await grantResponse.Content.ReadAsStringAsync());
        var grant = await ReadAsync<BuilderAuthorizationResult>(grantResponse);

        Assert.IsFalse(grant.IsReplay);
        Assert.AreEqual(operationId, grant.ClientOperationId);
        Assert.AreEqual(fixture.ProjectId, grant.Authorization.ProjectId);
        Assert.AreEqual(1, grant.Authorization.ActorUserId);
        Assert.AreEqual(package.Core.Id, grant.Authorization.BuilderWorkPackageCoreId);
        Assert.AreEqual(package.BuilderWorkPackageCoreHash,
            grant.Authorization.BuilderWorkPackageCoreHash);
        Assert.IsTrue(grant.Authorization.SingleUse);
        Assert.IsNull(grant.Authorization.ConsumedAtUtc);
        Assert.IsNull(grant.Authorization.ConsumedByBuilderExecutionRunId);
        Assert.IsNull(grant.Authorization.RevokedAtUtc);
        Assert.AreEqual(BuilderExecutionAuthorizationStates.Valid, grant.Authorization.State);
        Assert.AreEqual(BuilderAuthorizationReasonCodes.Ready, grant.Authorization.ReasonCode);
        Assert.AreEqual(
            TimeSpan.FromMinutes(15),
            grant.Authorization.ExpiresAtUtc - grant.Authorization.GrantedAtUtc);
        Assert.AreEqual(package.Core.Id, grant.WorkPackage.Core.Id);
        Assert.AreEqual(hashBefore, grant.WorkPackage.CoreSha256);
        Assert.AreEqual(grant.Authorization.Id, grant.WorkPackage.SingleUseAuthorizationId);
        Assert.AreEqual(grant.Authorization.GrantedAtUtc, grant.WorkPackage.AuthorizedAtUtc.UtcDateTime);
        Assert.AreEqual(grant.Authorization.ExpiresAtUtc, grant.WorkPackage.ExpiresAtUtc.UtcDateTime);
        Assert.IsTrue(grant.WorkPackage.SingleUse);

        var canonicalAfter = BuilderWorkPackageCoreCodec.SerializeCanonical(package.Core);
        Assert.AreEqual(canonicalBefore, canonicalAfter);
        Assert.AreEqual(hashBefore, BuilderWorkPackageCoreCodec.ComputeHash(package.Core));
        Assert.AreEqual(hashBefore, grant.Authorization.BuilderWorkPackageCoreHash);
        AssertCoreIsAuthorizationFree(canonicalAfter);

        await ExpireLeaseAsync(fixture);
        var reopenResponse = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{fixture.ProjectId}/open",
            new { clientOperationId = Guid.NewGuid(), takeOver = true });
        Assert.AreEqual(
            HttpStatusCode.OK,
            reopenResponse.StatusCode,
            await reopenResponse.Content.ReadAsStringAsync());
        var reopened = await ReadAsync<WorkbenchProjectEntryContext>(reopenResponse);
        Assert.IsTrue(reopened.LeaseEpoch > fixture.LeaseEpoch);
        var renewed = fixture with
        {
            WorkbenchSessionId = reopened.WorkbenchSessionId,
            LeaseEpoch = reopened.LeaseEpoch
        };

        var replayResponse = await client.PostAsJsonAsync(
            AuthorizationsUrl(fixture.ProjectId),
            AuthorizationPayload(renewed, operationId, package));
        Assert.AreEqual(
            HttpStatusCode.OK,
            replayResponse.StatusCode,
            await replayResponse.Content.ReadAsStringAsync());
        var replay = await ReadAsync<BuilderAuthorizationResult>(replayResponse);
        Assert.IsTrue(replay.IsReplay);
        Assert.AreEqual(grant.Authorization.Id, replay.Authorization.Id);
        Assert.AreEqual(grant.Authorization.GrantedAtUtc, replay.Authorization.GrantedAtUtc);
        Assert.AreEqual(grant.Authorization.ExpiresAtUtc, replay.Authorization.ExpiresAtUtc);

        await AssertPr07AWriteCountsAsync(
            fixture.ProjectId,
            cores: 1,
            ticketReferences: 1,
            authorizations: 1,
            completedOperations: 2);
    }

    [TestMethod]
    public async Task RevokeAuthorization_IsFenced_Idempotent_AndLeavesNoBuilderExecution()
    {
        using var scenario = new AuthorizationScenario();
        using var factory = scenario.CreateFactory();
        using var client = await AuthenticatedClientAsync(factory);
        var fixture = await CreateReadyDeliveryProjectAsync(
            client,
            "Revoke Builder authorization",
            scenario.Observer,
            scenario.Branch,
            "Sign in");
        var package = await CreateWorkPackageAsync(
            client,
            fixture,
            Guid.NewGuid(),
            [fixture.Tickets[0].Id]);
        var grant = await GrantAsync(client, fixture, package, Guid.NewGuid());
        var operationId = Guid.NewGuid();

        var staleFence = await client.PostAsJsonAsync(
            RevocationsUrl(fixture.ProjectId, grant.Authorization.Id),
            new
            {
                fixture.WorkbenchSessionId,
                leaseEpoch = fixture.LeaseEpoch + 1,
                clientOperationId = Guid.NewGuid()
            });
        Assert.AreEqual(
            HttpStatusCode.Conflict,
            staleFence.StatusCode,
            await staleFence.Content.ReadAsStringAsync());
        await AssertErrorAsync(staleFence, WorkbenchLeaseFenceException.ErrorCode);

        var revokeResponse = await client.PostAsJsonAsync(
            RevocationsUrl(fixture.ProjectId, grant.Authorization.Id),
            FencePayload(fixture, operationId));
        Assert.AreEqual(
            HttpStatusCode.OK,
            revokeResponse.StatusCode,
            await revokeResponse.Content.ReadAsStringAsync());
        var revoked = await ReadAsync<BuilderAuthorizationRevocationResult>(revokeResponse);
        Assert.IsFalse(revoked.IsReplay);
        Assert.AreEqual(BuilderExecutionAuthorizationStates.Revoked,
            revoked.Authorization.State);
        Assert.AreEqual(BuilderAuthorizationReasonCodes.AuthorizationRevoked,
            revoked.Authorization.ReasonCode);
        Assert.IsNotNull(revoked.Authorization.RevokedAtUtc);
        Assert.IsNull(revoked.Authorization.ConsumedAtUtc);

        var replayResponse = await client.PostAsJsonAsync(
            RevocationsUrl(fixture.ProjectId, grant.Authorization.Id),
            FencePayload(fixture, operationId));
        Assert.AreEqual(
            HttpStatusCode.OK,
            replayResponse.StatusCode,
            await replayResponse.Content.ReadAsStringAsync());
        var replay = await ReadAsync<BuilderAuthorizationRevocationResult>(replayResponse);
        Assert.IsTrue(replay.IsReplay);
        Assert.AreEqual(revoked.Authorization.Id, replay.Authorization.Id);
        Assert.AreEqual(revoked.Authorization.RevokedAtUtc, replay.Authorization.RevokedAtUtc);

        var context = await ReadContextAsync(
            client,
            fixture.ProjectId,
            fixture.Tickets[0].Id);
        Assert.IsNotNull(context.Authorization);
        Assert.AreEqual(revoked.Authorization.Id, context.Authorization.Id);
        Assert.AreEqual(BuilderExecutionAuthorizationStates.Revoked,
            context.Authorization.State);

        await AssertPr07AWriteCountsAsync(
            fixture.ProjectId,
            cores: 1,
            ticketReferences: 1,
            authorizations: 1,
            completedOperations: 3);
    }

    [TestMethod]
    public async Task AuthorizationExpires_FromServerClock_WithoutChangingReadinessOrCreatingARun()
    {
        using var scenario = new AuthorizationScenario();
        using var factory = scenario.CreateFactory();
        using var client = await AuthenticatedClientAsync(factory);
        var fixture = await CreateReadyDeliveryProjectAsync(
            client,
            "Expire Builder authorization",
            scenario.Observer,
            scenario.Branch,
            "Sign in");
        var package = await CreateWorkPackageAsync(
            client,
            fixture,
            Guid.NewGuid(),
            [fixture.Tickets[0].Id]);
        var granted = await GrantAsync(client, fixture, package, Guid.NewGuid());

        scenario.Clock.Advance(TimeSpan.FromMinutes(15).Add(TimeSpan.FromSeconds(1)));
        var expired = await ReadContextAsync(
            client,
            fixture.ProjectId,
            fixture.Tickets[0].Id);
        Assert.AreEqual(ProjectExecutionReadinessStates.Ready,
            expired.ExecutionReadiness);
        Assert.IsNotNull(expired.Authorization);
        Assert.AreEqual(granted.Authorization.Id, expired.Authorization.Id);
        Assert.AreEqual(BuilderExecutionAuthorizationStates.Expired,
            expired.Authorization.State);
        Assert.AreEqual(BuilderAuthorizationReasonCodes.AuthorizationExpired,
            expired.Authorization.ReasonCode);

        var replacement = await GrantAsync(
            client,
            fixture,
            package,
            Guid.NewGuid());
        Assert.AreNotEqual(granted.Authorization.Id, replacement.Authorization.Id);
        Assert.AreEqual(BuilderExecutionAuthorizationStates.Valid,
            replacement.Authorization.State);

        await AssertPr07AWriteCountsAsync(
            fixture.ProjectId,
            cores: 1,
            ticketReferences: 1,
            authorizations: 2,
            completedOperations: 3);
    }

    [TestMethod]
    public async Task TicketRevisionDrift_InvalidatesExistingAuthorization_AndRejectsNewGrantForOldCore()
    {
        using var scenario = new AuthorizationScenario();
        using var factory = scenario.CreateFactory();
        using var client = await AuthenticatedClientAsync(factory);
        var fixture = await CreateReadyDeliveryProjectAsync(
            client,
            "Ticket drift",
            scenario.Observer,
            scenario.Branch,
            "Sign in");
        var package = await CreateWorkPackageAsync(
            client,
            fixture,
            Guid.NewGuid(),
            [fixture.Tickets[0].Id]);
        var authorization = await GrantAsync(client, fixture, package, Guid.NewGuid());

        var ticket = fixture.Tickets[0];
        ticket.Summary = "Revision two changes the exact Builder scope.";
        var updateResponse = await client.PatchAsJsonAsync(
            $"/api/projects/{fixture.ProjectId}/tickets/{ticket.Id}",
            ticket);
        Assert.AreEqual(
            HttpStatusCode.OK,
            updateResponse.StatusCode,
            await updateResponse.Content.ReadAsStringAsync());
        var updated = await ReadAsync<ProjectTicket>(updateResponse);
        Assert.AreEqual(2L, updated.Revision);

        var context = await ReadContextAsync(client, fixture.ProjectId, ticket.Id);
        Assert.IsNotNull(context.Authorization);
        Assert.AreEqual(authorization.Authorization.Id, context.Authorization.Id);
        Assert.AreEqual(BuilderExecutionAuthorizationStates.ScopeStale,
            context.Authorization.State);
        Assert.AreEqual(BuilderAuthorizationReasonCodes.TicketRevisionChanged,
            context.Authorization.ReasonCode);

        var rejected = await client.PostAsJsonAsync(
            AuthorizationsUrl(fixture.ProjectId),
            AuthorizationPayload(fixture, Guid.NewGuid(), package));
        Assert.AreEqual(
            HttpStatusCode.Conflict,
            rejected.StatusCode,
            await rejected.Content.ReadAsStringAsync());
        await AssertErrorAndReasonAsync(
            rejected,
            BuilderAuthorizationStaleScopeException.ErrorCode,
            BuilderAuthorizationReasonCodes.TicketRevisionChanged);

        await AssertPr07AWriteCountsAsync(
            fixture.ProjectId,
            cores: 1,
            ticketReferences: 1,
            authorizations: 1,
            completedOperations: 2);
    }

    [TestMethod]
    public async Task BaselineDrift_InvalidatesExistingAuthorization_AndRejectsNewGrantForOldCore()
    {
        using var scenario = new AuthorizationScenario();
        using var factory = scenario.CreateFactory();
        using var client = await AuthenticatedClientAsync(factory);
        var fixture = await CreateReadyDeliveryProjectAsync(
            client,
            "Baseline drift",
            scenario.Observer,
            scenario.Branch,
            "Sign in");
        var package = await CreateWorkPackageAsync(
            client,
            fixture,
            Guid.NewGuid(),
            [fixture.Tickets[0].Id]);
        var authorization = await GrantAsync(client, fixture, package, Guid.NewGuid());

        scenario.Branch.HeadCommit = new string('d', 40);

        var context = await ReadContextAsync(
            client,
            fixture.ProjectId,
            fixture.Tickets[0].Id);
        Assert.IsNotNull(context.Authorization);
        Assert.AreEqual(authorization.Authorization.Id, context.Authorization.Id);
        Assert.AreEqual(BuilderExecutionAuthorizationStates.ScopeStale,
            context.Authorization.State);
        Assert.AreEqual(BuilderAuthorizationReasonCodes.RepositoryBaselineChanged,
            context.Authorization.ReasonCode);

        var rejected = await client.PostAsJsonAsync(
            AuthorizationsUrl(fixture.ProjectId),
            AuthorizationPayload(fixture, Guid.NewGuid(), package));
        Assert.AreEqual(
            HttpStatusCode.Conflict,
            rejected.StatusCode,
            await rejected.Content.ReadAsStringAsync());
        await AssertErrorAndReasonAsync(
            rejected,
            BuilderAuthorizationStaleScopeException.ErrorCode,
            BuilderAuthorizationReasonCodes.RepositoryBaselineChanged);

        await AssertPr07AWriteCountsAsync(
            fixture.ProjectId,
            cores: 1,
            ticketReferences: 1,
            authorizations: 1,
            completedOperations: 2);
    }

    [TestMethod]
    public async Task ReadinessDrift_InvalidatesExistingAuthorization_AndRejectsNewGrantForOldCore()
    {
        using var scenario = new AuthorizationScenario();
        using var factory = scenario.CreateFactory();
        using var client = await AuthenticatedClientAsync(factory);
        var fixture = await CreateReadyDeliveryProjectAsync(
            client,
            "Readiness drift",
            scenario.Observer,
            scenario.Branch,
            "Sign in");
        var package = await CreateWorkPackageAsync(
            client,
            fixture,
            Guid.NewGuid(),
            [fixture.Tickets[0].Id]);
        var authorization = await GrantAsync(client, fixture, package, Guid.NewGuid());

        var newerSandboxResponse = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{fixture.ProjectId}/repository/sandbox-qualifications",
            new
            {
                fixture.WorkbenchSessionId,
                fixture.LeaseEpoch,
                clientOperationId = Guid.NewGuid(),
                expectedRepositoryBindingRevision = fixture.BindingRevision,
                expectedExecutionProfileRevision = fixture.ProfileRevision
            });
        Assert.AreEqual(
            HttpStatusCode.OK,
            newerSandboxResponse.StatusCode,
            await newerSandboxResponse.Content.ReadAsStringAsync());
        var newerSandbox =
            await ReadAsync<WorkbenchSandboxQualificationResult>(newerSandboxResponse);
        Assert.AreEqual(SandboxQualificationStates.Passed, newerSandbox.Attempt.State);

        var context = await ReadContextAsync(
            client,
            fixture.ProjectId,
            fixture.Tickets[0].Id);
        Assert.AreEqual(ProjectExecutionReadinessStates.ValidationRequired,
            context.ExecutionReadiness);
        Assert.IsNotNull(context.Authorization);
        Assert.AreEqual(authorization.Authorization.Id, context.Authorization.Id);
        Assert.AreEqual(BuilderExecutionAuthorizationStates.ScopeStale,
            context.Authorization.State);
        Assert.AreEqual(BuilderAuthorizationReasonCodes.TechnicalReadinessNotCurrent,
            context.Authorization.ReasonCode);

        var rejected = await client.PostAsJsonAsync(
            AuthorizationsUrl(fixture.ProjectId),
            AuthorizationPayload(fixture, Guid.NewGuid(), package));
        Assert.AreEqual(
            HttpStatusCode.Conflict,
            rejected.StatusCode,
            await rejected.Content.ReadAsStringAsync());
        await AssertErrorAndReasonAsync(
            rejected,
            BuilderAuthorizationStaleScopeException.ErrorCode,
            BuilderAuthorizationReasonCodes.TechnicalReadinessNotCurrent);

        await AssertPr07AWriteCountsAsync(
            fixture.ProjectId,
            cores: 1,
            ticketReferences: 1,
            authorizations: 1,
            completedOperations: 2);
    }

    [TestMethod]
    public async Task WorkPackageMutation_RequiresCurrentSessionLeaseAndMembership_WithoutPartialWrites()
    {
        using var scenario = new AuthorizationScenario();
        using var factory = scenario.CreateFactory();
        using var client = await AuthenticatedClientAsync(factory);
        var fixture = await CreateReadyDeliveryProjectAsync(
            client,
            "Builder mutation fences",
            scenario.Observer,
            scenario.Branch,
            "Sign in");

        var staleLease = await client.PostAsJsonAsync(
            WorkPackagesUrl(fixture.ProjectId),
            new
            {
                fixture.WorkbenchSessionId,
                leaseEpoch = fixture.LeaseEpoch + 1,
                clientOperationId = Guid.NewGuid(),
                ticketIds = new[] { fixture.Tickets[0].Id }
            });
        Assert.AreEqual(
            HttpStatusCode.Conflict,
            staleLease.StatusCode,
            await staleLease.Content.ReadAsStringAsync());
        await AssertErrorAsync(staleLease, WorkbenchLeaseFenceException.ErrorCode);
        await AssertPr07AWriteCountsAsync(
            fixture.ProjectId,
            cores: 0,
            ticketReferences: 0,
            authorizations: 0,
            completedOperations: 0);

        await using (var connection = new SqlConnection(ConnectionString))
        {
            await connection.ExecuteAsync(
                """
                DELETE FROM dbo.ProjectMembers
                WHERE TenantId=1 AND ProjectId=@ProjectId AND UserId=1;
                """,
                new { fixture.ProjectId });
        }

        var noMembership = await client.PostAsJsonAsync(
            WorkPackagesUrl(fixture.ProjectId),
            WorkPackagePayload(
                fixture,
                Guid.NewGuid(),
                [fixture.Tickets[0].Id]));
        Assert.AreEqual(
            HttpStatusCode.NotFound,
            noMembership.StatusCode,
            await noMembership.Content.ReadAsStringAsync());
        await AssertErrorAsync(noMembership, "project_not_found");
        await AssertPr07AWriteCountsAsync(
            fixture.ProjectId,
            cores: 0,
            ticketReferences: 0,
            authorizations: 0,
            completedOperations: 0);
    }

    [TestMethod]
    public async Task PrepareBuilderAgentRun_AtomicallyConsumesAuthorization_FreezesInput_AndReplays()
    {
        using var scenario = new AuthorizationScenario();
        using var factory = scenario.CreateFactory();
        using var client = await AuthenticatedClientAsync(factory);
        var fixture = await CreateReadyDeliveryProjectAsync(
            client, "Atomic Builder prompt preparation",
            scenario.Observer, scenario.Branch, "Sign in");
        var package = await CreateWorkPackageAsync(
            client, fixture, Guid.NewGuid(), [fixture.Tickets[0].Id]);
        var grant = await GrantAsync(client, fixture, package, Guid.NewGuid());
        var operationId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync(
            AgentRunsUrl(fixture.ProjectId),
            PreparationPayload(fixture, operationId, grant, package));
        Assert.AreEqual(
            HttpStatusCode.Created, response.StatusCode,
            await response.Content.ReadAsStringAsync());
        var prepared = await ReadAsync<PreparedBuilderAgentRun>(response);
        Assert.AreEqual(BuilderAgentRunStates.Prepared, prepared.Status);
        Assert.AreEqual(grant.Authorization.Id, prepared.BuilderExecutionAuthorizationId);
        Assert.AreEqual(package.Core.Id, prepared.BuilderWorkPackageCoreId);
        Assert.AreEqual(package.BuilderWorkPackageCoreHash, prepared.BuilderWorkPackageCoreSha256);
        Assert.AreEqual(prepared.PreparedAtUtc, prepared.ProviderInvocationPermittedAtUtc);
        Assert.IsTrue(new[]
        {
            prepared.EffectiveProfileSha256,
            prepared.RoleContextSha256,
            prepared.PromptSha256,
            prepared.ToolManifestSha256,
            prepared.ProviderInputSha256
        }.All(static hash => hash.Length == 64));

        await using (var connection = new SqlConnection(ConnectionString))
        {
            var row = await connection.QuerySingleAsync<PreparedRunDatabaseRow>(
                """
                SELECT run.Status, run.ProviderInvokedAtUtc,
                       run.RoleContextJson, run.ToolManifestJson,
                       authz.ConsumedAtUtc,
                       authz.ConsumedByBuilderExecutionRunId
                FROM dbo.BuilderAgentRuns run
                INNER JOIN dbo.BuilderExecutionAuthorizations authz
                    ON authz.Id=run.BuilderExecutionAuthorizationId
                WHERE run.Id=@BuilderAgentRunId;
                """,
                new { prepared.BuilderAgentRunId });
            Assert.AreEqual(BuilderAgentRunStates.Prepared, row.Status);
            Assert.IsNull(row.ProviderInvokedAtUtc);
            Assert.IsNotNull(row.ConsumedAtUtc);
            Assert.AreEqual(prepared.BuilderAgentRunId, row.ConsumedByBuilderExecutionRunId);
            StringAssert.Contains(row.RoleContextJson, "\"acceptanceCriteria\"");
            StringAssert.Contains(row.RoleContextJson, "\"singleUseAuthorizationId\"");
            StringAssert.Contains(row.ToolManifestJson, "\"mayWriteActiveRepository\":false");
        }

        var replayResponse = await client.PostAsJsonAsync(
            AgentRunsUrl(fixture.ProjectId),
            PreparationPayload(fixture, operationId, grant, package));
        Assert.AreEqual(
            HttpStatusCode.OK, replayResponse.StatusCode,
            await replayResponse.Content.ReadAsStringAsync());
        var replay = await ReadAsync<PreparedBuilderAgentRun>(replayResponse);
        Assert.IsTrue(replay.IsReplay);
        Assert.AreEqual(prepared.BuilderAgentRunId, replay.BuilderAgentRunId);

        var secondUse = await client.PostAsJsonAsync(
            AgentRunsUrl(fixture.ProjectId),
            PreparationPayload(fixture, Guid.NewGuid(), grant, package));
        Assert.AreEqual(
            HttpStatusCode.Conflict, secondUse.StatusCode,
            await secondUse.Content.ReadAsStringAsync());
        await AssertErrorAndReasonAsync(
            secondUse,
            BuilderPromptPreparationConflictException.ErrorCode,
            BuilderPromptPreparationReasonCodes.AuthorizationConsumed);
    }

    [TestMethod]
    public async Task PrepareBuilderAgentRun_RefusesChangedBaseline_BeforeRunOrConsumption()
    {
        using var scenario = new AuthorizationScenario();
        using var factory = scenario.CreateFactory();
        using var client = await AuthenticatedClientAsync(factory);
        var fixture = await CreateReadyDeliveryProjectAsync(
            client, "Stale Builder prompt preparation",
            scenario.Observer, scenario.Branch, "Sign in");
        var package = await CreateWorkPackageAsync(
            client, fixture, Guid.NewGuid(), [fixture.Tickets[0].Id]);
        var grant = await GrantAsync(client, fixture, package, Guid.NewGuid());
        scenario.Branch.HeadCommit = new string('f', 40);

        var response = await client.PostAsJsonAsync(
            AgentRunsUrl(fixture.ProjectId),
            PreparationPayload(fixture, Guid.NewGuid(), grant, package));
        Assert.AreEqual(
            HttpStatusCode.Conflict, response.StatusCode,
            await response.Content.ReadAsStringAsync());
        await AssertErrorAndReasonAsync(
            response,
            BuilderPromptPreparationConflictException.ErrorCode,
            BuilderPromptPreparationReasonCodes.RepositoryBaselineChanged);

        await using var connection = new SqlConnection(ConnectionString);
        var runCount = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.BuilderAgentRuns WHERE TenantId=1 AND ProjectId=@ProjectId;",
            new { fixture.ProjectId });
        var consumedCount = await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM dbo.BuilderExecutionAuthorizations
            WHERE TenantId=1 AND ProjectId=@ProjectId AND ConsumedAtUtc IS NOT NULL;
            """,
            new { fixture.ProjectId });
        Assert.AreEqual(0, runCount);
        Assert.AreEqual(0, consumedCount);
    }

    [TestMethod]
    public async Task ExecuteBuilderAgentRun_ClaimsOnce_PersistsProposalAndSandboxEvidence_AndReplays()
    {
        using var scenario = new AuthorizationScenario();
        using var factory = scenario.CreateFactory();
        using var client = await AuthenticatedClientAsync(factory);
        var fixture = await CreateReadyDeliveryProjectAsync(
            client, "Real Builder execution boundary",
            scenario.Observer, scenario.Branch, "Sign in");
        var package = await CreateWorkPackageAsync(
            client, fixture, Guid.NewGuid(), [fixture.Tickets[0].Id]);
        var grant = await GrantAsync(client, fixture, package, Guid.NewGuid());
        var preparationResponse = await client.PostAsJsonAsync(
            AgentRunsUrl(fixture.ProjectId),
            PreparationPayload(fixture, Guid.NewGuid(), grant, package));
        var prepared = await ReadAsync<PreparedBuilderAgentRun>(preparationResponse);
        var operationId = Guid.NewGuid();
        var payload = new
        {
            fixture.WorkbenchSessionId,
            fixture.LeaseEpoch,
            clientOperationId = operationId,
            expectedProviderInputSha256 = prepared.ProviderInputSha256
        };

        var response = await client.PostAsJsonAsync(
            ExecutionUrl(fixture.ProjectId, prepared.BuilderAgentRunId), payload);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
            await response.Content.ReadAsStringAsync());
        var result = await ReadAsync<BuilderExecutionResult>(response);
        Assert.AreEqual(BuilderAgentRunTerminalStates.Succeeded, result.Status);
        Assert.AreEqual(1, result.AttemptCount);
        Assert.AreEqual(1, result.ProposedFiles.Count);
        Assert.AreEqual("src/App.cs", result.ProposedFiles[0].RelativePath);
        Assert.AreEqual(3, result.ToolCalls.Count);
        Assert.AreEqual(64, result.RawPatchSha256.Length);
        Assert.AreEqual(64, result.SandboxEvidenceManifestSha256!.Length);
        Assert.AreEqual(1, scenario.Gateway.InvocationCount);
        Assert.AreEqual(1, scenario.BuilderSandbox.InvocationCount);

        var replayResponse = await client.PostAsJsonAsync(
            ExecutionUrl(fixture.ProjectId, prepared.BuilderAgentRunId), payload);
        var replay = await ReadAsync<BuilderExecutionResult>(replayResponse);
        Assert.IsTrue(replay.IsReplay);
        Assert.AreEqual(1, scenario.Gateway.InvocationCount);

        var secondClaim = await client.PostAsJsonAsync(
            ExecutionUrl(fixture.ProjectId, prepared.BuilderAgentRunId),
            new
            {
                fixture.WorkbenchSessionId,
                fixture.LeaseEpoch,
                clientOperationId = Guid.NewGuid(),
                expectedProviderInputSha256 = prepared.ProviderInputSha256
            });
        Assert.AreEqual(HttpStatusCode.Conflict, secondClaim.StatusCode);

        await using var connection = new SqlConnection(ConnectionString);
        var counts = await connection.QuerySingleAsync<(int Attempts, int Files, int Tools)>(
            """
            SELECT
              (SELECT COUNT(*) FROM dbo.BuilderAgentRunAttempts WHERE BuilderAgentRunId=@RunId),
              (SELECT COUNT(*) FROM dbo.BuilderAgentRunProposedFiles WHERE BuilderAgentRunId=@RunId),
              (SELECT COUNT(*) FROM dbo.BuilderAgentRunToolCalls WHERE BuilderAgentRunId=@RunId);
            """, new { RunId = prepared.BuilderAgentRunId });
        Assert.AreEqual(1, counts.Attempts);
        Assert.AreEqual(1, counts.Files);
        Assert.AreEqual(3, counts.Tools);
    }

    private static async Task<ReadyDeliveryFixture> CreateReadyDeliveryProjectAsync(
        HttpClient client,
        string name,
        ControllableReadinessObserver observer,
        ControllableBranchObserver branch,
        params string[] ticketTitles)
    {
        var startResponse = await client.PostAsJsonAsync(
            "/api/projects/start",
            new { clientOperationId = Guid.NewGuid(), name });
        Assert.AreEqual(
            HttpStatusCode.Created,
            startResponse.StatusCode,
            await startResponse.Content.ReadAsStringAsync());
        var start = await ReadAsync<WorkbenchProjectEntryContext>(startResponse);

        var planResponse = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{start.ProjectId}/repository/setup-plans",
            new
            {
                start.WorkbenchSessionId,
                start.LeaseEpoch,
                profileDefinitionId = ProfileDefinitionId
            });
        Assert.AreEqual(
            HttpStatusCode.OK,
            planResponse.StatusCode,
            await planResponse.Content.ReadAsStringAsync());
        var plan = await ReadAsync<RepositorySetupPlanPreview>(planResponse);

        var confirmResponse = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{start.ProjectId}/repository/setup-confirmations",
            new
            {
                start.WorkbenchSessionId,
                start.LeaseEpoch,
                clientOperationId = Guid.NewGuid(),
                expectedPlanHash = plan.PlanHash
            });
        Assert.AreEqual(
            HttpStatusCode.OK,
            confirmResponse.StatusCode,
            await confirmResponse.Content.ReadAsStringAsync());
        var confirmation = await ReadAsync<RepositorySetupConfirmationResult>(confirmResponse);

        var provisionResponse = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{start.ProjectId}/repository/provisionings",
            new
            {
                start.WorkbenchSessionId,
                start.LeaseEpoch,
                clientOperationId = Guid.NewGuid(),
                setupConfirmationId = confirmation.ConfirmationId,
                expectedRepositoryBindingRevision = confirmation.RepositoryBinding.Revision,
                expectedExecutionProfileRevision = confirmation.ExecutionProfile.Revision
            });
        Assert.AreEqual(
            HttpStatusCode.OK,
            provisionResponse.StatusCode,
            await provisionResponse.Content.ReadAsStringAsync());
        var provisioned = await ReadAsync<RepositoryProvisioningResult>(provisionResponse);
        observer.GitTreeId = provisioned.GitTreeId;
        branch.HeadCommit = provisioned.BaselineCommit;
        var fixture = new ReadyDeliveryFixture(
            start.ProjectId,
            start.WorkbenchSessionId,
            start.LeaseEpoch,
            provisioned.RepositoryBinding.Revision,
            provisioned.ExecutionProfile.Revision,
            []);

        var sandboxResponse = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{fixture.ProjectId}/repository/sandbox-qualifications",
            new
            {
                fixture.WorkbenchSessionId,
                fixture.LeaseEpoch,
                clientOperationId = Guid.NewGuid(),
                expectedRepositoryBindingRevision = fixture.BindingRevision,
                expectedExecutionProfileRevision = fixture.ProfileRevision
            });
        Assert.AreEqual(
            HttpStatusCode.OK,
            sandboxResponse.StatusCode,
            await sandboxResponse.Content.ReadAsStringAsync());
        var sandbox = await ReadAsync<WorkbenchSandboxQualificationResult>(sandboxResponse);
        Assert.AreEqual(SandboxQualificationStates.Passed, sandbox.Attempt.State);

        var readinessResponse = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{fixture.ProjectId}/repository/readiness-validations",
            new
            {
                fixture.WorkbenchSessionId,
                fixture.LeaseEpoch,
                clientOperationId = Guid.NewGuid(),
                expectedRepositoryBindingRevision = fixture.BindingRevision,
                expectedExecutionProfileRevision = fixture.ProfileRevision
            });
        Assert.AreEqual(
            HttpStatusCode.OK,
            readinessResponse.StatusCode,
            await readinessResponse.Content.ReadAsStringAsync());
        var readiness = await ReadAsync<RefreshRepositoryReadinessResult>(readinessResponse);
        Assert.AreEqual(ProjectExecutionReadinessStates.Ready,
            readiness.Evaluation.ExecutionReadiness);

        var tickets = new List<ProjectTicket>();
        foreach (var title in ticketTitles)
        {
            var ticketResponse = await client.PostAsJsonAsync(
                $"/api/projects/{fixture.ProjectId}/tickets",
                new CreateProjectTicketRequest
                {
                    Title = title,
                    Summary = $"{title} is required.",
                     Problem = $"The project does not yet support {title}.",
                     ProposedChange = $"Implement {title}.",
                     AcceptanceCriteria = [$"{title} is demonstrably complete."],
                     LinkedFilePaths = ["src/App.cs", "tests/AppTests.cs"]
                 });
            Assert.AreEqual(
                HttpStatusCode.OK,
                ticketResponse.StatusCode,
                await ticketResponse.Content.ReadAsStringAsync());
            tickets.Add(await ReadAsync<ProjectTicket>(ticketResponse));
        }

        await using (var connection = new SqlConnection(ConnectionString))
        {
            await connection.ExecuteAsync(
                """
                INSERT dbo.ProjectLifecyclePhases
                    (TenantId, ProjectId, Revision, Phase,
                     ChangedByActorUserId, ChangedAtUtc)
                SELECT 1, @ProjectId, MAX(Revision) + 1, N'Delivery',
                       1, SYSUTCDATETIME()
                FROM dbo.ProjectLifecyclePhases
                WHERE TenantId=1 AND ProjectId=@ProjectId
                HAVING MAX(CASE WHEN Phase=N'Delivery' THEN 1 ELSE 0 END)=0;
                """,
                new { fixture.ProjectId });
        }

        return fixture with { Tickets = tickets };
    }

    private static async Task<BuilderWorkPackageResult> CreateWorkPackageAsync(
        HttpClient client,
        ReadyDeliveryFixture fixture,
        Guid operationId,
        IReadOnlyList<long> ticketIds)
    {
        var response = await client.PostAsJsonAsync(
            WorkPackagesUrl(fixture.ProjectId),
            WorkPackagePayload(fixture, operationId, ticketIds));
        Assert.AreEqual(
            HttpStatusCode.Created,
            response.StatusCode,
            await response.Content.ReadAsStringAsync());
        return await ReadAsync<BuilderWorkPackageResult>(response);
    }

    private static async Task<BuilderAuthorizationResult> GrantAsync(
        HttpClient client,
        ReadyDeliveryFixture fixture,
        BuilderWorkPackageResult package,
        Guid operationId)
    {
        var response = await client.PostAsJsonAsync(
            AuthorizationsUrl(fixture.ProjectId),
            AuthorizationPayload(fixture, operationId, package));
        Assert.AreEqual(
            HttpStatusCode.Created,
            response.StatusCode,
            await response.Content.ReadAsStringAsync());
        return await ReadAsync<BuilderAuthorizationResult>(response);
    }

    private static async Task<WorkbenchBuilderContext> ReadContextAsync(
        HttpClient client,
        int projectId,
        long? ticketId)
    {
        var suffix = ticketId.HasValue ? $"?ticketId={ticketId.Value}" : string.Empty;
        var response = await client.GetAsync(
            $"/api/workbench/projects/{projectId}/builder{suffix}");
        Assert.AreEqual(
            HttpStatusCode.OK,
            response.StatusCode,
            await response.Content.ReadAsStringAsync());
        return await ReadAsync<WorkbenchBuilderContext>(response);
    }

    private static async Task ExpireLeaseAsync(ReadyDeliveryFixture fixture)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.ExecuteAsync(
            """
            UPDATE dbo.WorkbenchWriteLeases
            SET ExpiresAtUtc=DATEADD(MINUTE, -1, SYSUTCDATETIME())
            WHERE TenantId=1 AND ProjectId=@ProjectId
              AND WorkbenchSessionId=@WorkbenchSessionId
              AND LeaseEpoch=@LeaseEpoch;
            """,
            fixture);
    }

    private static object WorkPackagePayload(
        ReadyDeliveryFixture fixture,
        Guid operationId,
        IReadOnlyList<long> ticketIds) => new
    {
        fixture.WorkbenchSessionId,
        fixture.LeaseEpoch,
        clientOperationId = operationId,
        ticketIds
    };

    private static object AuthorizationPayload(
        ReadyDeliveryFixture fixture,
        Guid operationId,
        BuilderWorkPackageResult package) => new
    {
        fixture.WorkbenchSessionId,
        fixture.LeaseEpoch,
        clientOperationId = operationId,
        builderWorkPackageCoreId = package.Core.Id,
        expectedCoreHash = package.BuilderWorkPackageCoreHash
    };

    private static object PreparationPayload(
        ReadyDeliveryFixture fixture,
        Guid operationId,
        BuilderAuthorizationResult grant,
        BuilderWorkPackageResult package) => new
    {
        fixture.WorkbenchSessionId,
        fixture.LeaseEpoch,
        clientOperationId = operationId,
        builderExecutionAuthorizationId = grant.Authorization.Id,
        builderWorkPackageCoreId = package.Core.Id,
        expectedCoreSha256 = package.BuilderWorkPackageCoreHash
    };

    private static object FencePayload(ReadyDeliveryFixture fixture, Guid operationId) => new
    {
        fixture.WorkbenchSessionId,
        fixture.LeaseEpoch,
        clientOperationId = operationId
    };

    private static string WorkPackagesUrl(int projectId) =>
        $"/api/workbench/projects/{projectId}/builder/work-packages";

    private static string AuthorizationsUrl(int projectId) =>
        $"/api/workbench/projects/{projectId}/builder/authorizations";

    private static string AgentRunsUrl(int projectId) =>
        $"/api/workbench/projects/{projectId}/builder/agent-runs";

    private static string ExecutionUrl(int projectId, Guid runId) =>
        $"/api/workbench/projects/{projectId}/builder/agent-runs/{runId:D}/executions";

    private static string RevocationsUrl(int projectId, Guid authorizationId) =>
        $"/api/workbench/projects/{projectId}/builder/authorizations/{authorizationId:D}/revocations";

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        var result = await response.Content.ReadFromJsonAsync<T>();
        Assert.IsNotNull(result);
        return result;
    }

    private static async Task AssertErrorAsync(
        HttpResponseMessage response,
        string expectedError)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual(expectedError, body.GetProperty("error").GetString());
    }

    private static async Task AssertErrorAndReasonAsync(
        HttpResponseMessage response,
        string expectedError,
        string expectedReason)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual(expectedError, body.GetProperty("error").GetString());
        Assert.AreEqual(expectedReason, body.GetProperty("reasonCode").GetString());
    }

    private static void AssertCoreIsAuthorizationFree(string canonicalJson)
    {
        var forbiddenNames = new[]
        {
            "Authorization",
            "ActorUserId",
            "WorkbenchSessionId",
            "LeaseEpoch",
            "ClientOperationId",
            "GrantedAtUtc",
            "ExpiresAtUtc",
            "RevokedAtUtc",
            "ConsumedAtUtc"
        };
        foreach (var name in forbiddenNames)
        {
            Assert.IsFalse(
                canonicalJson.Contains(name, StringComparison.Ordinal),
                $"Canonical core JSON must not include {name}.");
        }
    }

    private static async Task AssertPr07AWriteCountsAsync(
        int projectId,
        int cores,
        int ticketReferences,
        int authorizations,
        int completedOperations)
    {
        await using var connection = new SqlConnection(ConnectionString);
        var counts = await connection.QuerySingleAsync<Pr07AWriteCounts>(
            """
            SELECT
              (SELECT COUNT(1) FROM dbo.BuilderWorkPackageCores
               WHERE TenantId=1 AND ProjectId=@ProjectId) AS Cores,
              (SELECT COUNT(1) FROM dbo.BuilderWorkPackageTickets
               WHERE TenantId=1 AND ProjectId=@ProjectId) AS TicketReferences,
              (SELECT COUNT(1) FROM dbo.BuilderExecutionAuthorizations
               WHERE TenantId=1 AND ProjectId=@ProjectId) AS Authorizations,
              (SELECT COUNT(1) FROM dbo.ClientOperations
               WHERE TenantId=1 AND ResultProjectId=@ProjectId
                 AND OperationKind IN
                     (N'CreateBuilderWorkPackage',
                      N'GrantBuilderExecutionAuthorization',
                      N'RevokeBuilderExecutionAuthorization')
                 AND Status=N'Completed') AS CompletedOperations,
              (SELECT COUNT(1) FROM dbo.Runs
               WHERE ProjectId=@ProjectId) AS LegacyRuns,
              (SELECT COUNT(1) FROM dbo.WorkbenchAgentRuns
               WHERE TenantId=1 AND ProjectId=@ProjectId) AS BuilderRuns,
              (SELECT COUNT(1)
               FROM dbo.WorkbenchAgentRunAttempts attempt
               INNER JOIN dbo.WorkbenchAgentRuns run
                 ON run.AgentRunId=attempt.AgentRunId
               WHERE run.TenantId=1 AND run.ProjectId=@ProjectId) AS BuilderAttempts;
            """,
            new { ProjectId = projectId });
        Assert.AreEqual(cores, counts.Cores);
        Assert.AreEqual(ticketReferences, counts.TicketReferences);
        Assert.AreEqual(authorizations, counts.Authorizations);
        Assert.AreEqual(completedOperations, counts.CompletedOperations);
        Assert.AreEqual(0, counts.LegacyRuns);
        Assert.AreEqual(0, counts.BuilderRuns);
        Assert.AreEqual(0, counts.BuilderAttempts);
    }

    private static async Task<HttpClient> AuthenticatedClientAsync(
        WebApplicationFactory<Program> factory)
    {
        var token = await SelectTenantAsync(await LoginAsync());
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private sealed class AuthorizationScenario : IDisposable
    {
        private readonly string _root = CreateRoot();
        public ControllableReadinessObserver Observer { get; } = new();
        public ControllableBranchObserver Branch { get; } = new();
        public AdjustableTimeProvider Clock { get; } = new();
        public PassingSandboxExecutionService Sandbox { get; } = new();
        public PassingBuilderModelGateway Gateway { get; } = new();
        public PassingBuilderSandboxRunner BuilderSandbox { get; } = new();

        public WebApplicationFactory<Program> CreateFactory()
        {
            var repositoryRoot = Path.Combine(_root, "repositories");
            var snapshotRoot = Path.Combine(_root, "snapshots");
            var evidenceRoot = Path.Combine(_root, "evidence");
            Directory.CreateDirectory(repositoryRoot);
            Directory.CreateDirectory(snapshotRoot);
            Directory.CreateDirectory(evidenceRoot);
            return Factory.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("WorkbenchV2:Enabled", "true");
                builder.UseSetting(
                    "WorkbenchRepositorySetup:ApprovedWorkspaceRoot",
                    repositoryRoot);
                builder.UseSetting("WorkbenchRepositoryProvisioning:GitExecutable", "git");
                builder.UseSetting("WorkbenchRepositoryProvisioning:GitTimeoutSeconds", "30");
                builder.UseSetting(
                    "WorkbenchProductionSandbox:SourceSnapshotRoot",
                    snapshotRoot);
                builder.UseSetting(
                    "WorkbenchProductionSandbox:EvidenceRoot",
                    evidenceRoot);
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<ISandboxRuntimePolicyCatalog>();
                    services.AddSingleton<ISandboxRuntimePolicyCatalog, PassingPolicyCatalog>();
                    services.RemoveAll<ISandboxExecutionService>();
                    services.AddSingleton<ISandboxExecutionService>(Sandbox);
                    services.RemoveAll<IRepositoryReadinessObserver>();
                    services.AddSingleton<IRepositoryReadinessObserver>(Observer);
                    services.RemoveAll<IBuilderStableConfigurationProvider>();
                    services.AddSingleton<IBuilderStableConfigurationProvider,
                        StableBuilderConfiguration>();
                    services.RemoveAll<IExecutionAvailabilityChecker>();
                    services.AddSingleton<IExecutionAvailabilityChecker,
                        AvailableExecutionChecker>();
                    services.RemoveAll<IBuilderRepositoryBranchObserver>();
                    services.AddSingleton<IBuilderRepositoryBranchObserver>(Branch);
                    services.RemoveAll<IWorkbenchBuilderModelGateway>();
                    services.AddSingleton<IWorkbenchBuilderModelGateway>(Gateway);
                    services.RemoveAll<IWorkbenchBuilderSandboxRunner>();
                    services.AddSingleton<IWorkbenchBuilderSandboxRunner>(BuilderSandbox);
                    services.RemoveAll<TimeProvider>();
                    services.AddSingleton<TimeProvider>(Clock);
                });
            });
        }

        public void Dispose() => DeleteRoot(_root);

        private static string CreateRoot()
        {
            var drive = Path.GetPathRoot(Environment.SystemDirectory)
                        ?? throw new InvalidOperationException(
                            "A system drive is required.");
            var root = Path.Combine(
                drive,
                "IronDev.BuilderAuthorization.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        private static void DeleteRoot(string root)
        {
            if (!Directory.Exists(root))
                return;
            var drive = Path.GetPathRoot(Environment.SystemDirectory)!;
            var safeBase = Path.GetFullPath(Path.Combine(
                drive,
                "IronDev.BuilderAuthorization.Tests"));
            var exact = Path.GetFullPath(root);
            if (!exact.StartsWith(
                    safeBase + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(exact, safeBase, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Refusing to remove a non-test Builder authorization root.");
            if (File.GetAttributes(exact).HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidOperationException(
                    "Refusing to remove a reparse-point Builder authorization test root.");
            foreach (var entry in Directory.EnumerateFileSystemEntries(
                         exact,
                         "*",
                         SearchOption.AllDirectories))
            {
                var attributes = File.GetAttributes(entry);
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                    throw new InvalidOperationException(
                        "Refusing to remove a test root containing a reparse point.");
                if (!attributes.HasFlag(FileAttributes.Directory) &&
                    attributes.HasFlag(FileAttributes.ReadOnly))
                    File.SetAttributes(entry, attributes & ~FileAttributes.ReadOnly);
            }
            Directory.Delete(exact, recursive: true);
        }
    }

    private sealed class PassingBuilderModelGateway : IWorkbenchBuilderModelGateway
    {
        public int InvocationCount { get; private set; }

        public Task<BuilderProviderResponse> InvokeAsync(
            BuilderPreparedExecutionInput input,
            int attemptNumber,
            string? repairEvidence,
            CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            var output = JsonSerializer.Serialize(new
            {
                schemaVersion = BuilderRoleContract.OutputSchemaVersion,
                proposedFiles = new[] { new { relativePath = "src/App.cs", content = "namespace App;" } }
            });
            return Task.FromResult(new BuilderProviderResponse(
                output, $"builder-{input.BuilderAgentRunId:N}-{attemptNumber}",
                "test-request", new IronDev.Core.Agents.AgentModelUsage(), true, 1));
        }
    }

    private sealed class PassingBuilderSandboxRunner : IWorkbenchBuilderSandboxRunner
    {
        public int InvocationCount { get; private set; }

        public Task<SandboxExecutionResult> ValidateAsync(
            BuilderSandboxValidationRequest request,
            CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            var completed = DateTimeOffset.UtcNow;
            var manifest = new SandboxEvidenceManifest
            {
                SchemaVersion = 1, ExecutionId = request.ExecutionId,
                ProjectId = request.WorkPackageCore.ProjectId,
                RepositoryBindingId = request.WorkPackageCore.RepositoryBindingId,
                RepositoryBindingRevision = request.WorkPackageCore.RepositoryBindingRevision,
                BaselineCommit = request.WorkPackageCore.BaselineCommit,
                WorktreeFingerprint = Hash("builder-proposal"),
                ProjectExecutionProfileId = request.WorkPackageCore.EffectiveProfile.ProjectExecutionProfileId,
                ProjectExecutionProfileRevision = request.WorkPackageCore.EffectiveProfile.ProjectExecutionProfileRevision,
                ProfileDefinitionId = request.WorkPackageCore.EffectiveProfile.ProfileDefinitionId,
                ProfileDescriptorRevision = request.WorkPackageCore.EffectiveProfile.ProfileDescriptorRevision,
                DescriptorSha256 = request.WorkPackageCore.EffectiveProfile.ProfileDescriptorSha256,
                TemplateBundleSha256 = request.WorkPackageCore.Sandbox.TemplateBundleSha256,
                ToolchainManifestId = request.WorkPackageCore.Sandbox.ToolchainManifestId,
                ContainerImageDigest = request.WorkPackageCore.Sandbox.QualifiedImageDigest,
                SandboxPolicyVersion = request.WorkPackageCore.Sandbox.PolicyVersion,
                SandboxPolicySha256 = request.WorkPackageCore.Sandbox.PolicySha256,
                TrustedSupervisorVersion = "test-supervisor",
                TrustedSupervisorSha256 = Hash("supervisor"),
                OfflineFeedManifestSha256 = request.WorkPackageCore.Sandbox.OfflineFeedManifestSha256,
                Status = SandboxExecutionStatus.Succeeded, ReasonCode = SandboxReasonCodes.Ready,
                SafeSummary = "Builder proposal passed.", StartedAtUtc = completed.AddSeconds(-1),
                CompletedAtUtc = completed,
                Inspection = new SandboxRuntimeInspection
                {
                    RuntimeName = "test", IsolationMode = SandboxIsolationModes.HcsHyperV,
                    ActualContainerImageDigest = request.WorkPackageCore.Sandbox.QualifiedImageDigest,
                    VirtualCpuCount = 2, MemoryMaximumMiB = 4096, WritableScratchMaximumGiB = 12,
                    MaximumUntrustedWorkloadProcessCount = 64, UntrustedWorkloadProcessScope = "sandbox",
                    TrustedSupervisorVersion = "test-supervisor", TrustedSupervisorSha256 = Hash("supervisor"),
                    SuspendedAssignmentBeforeResumeProven = true, UntrustedWorkloadProcessLimitProven = true,
                    RestrictedLowIntegrityWorkloadIdentityProven = true, SupervisorHandleIsolationProven = true,
                    WorkloadScratchAndEvidenceBoundaryProven = true, BrokerLaunchDenialProven = true,
                    ProjectBytesCopiedAfterPreflightProven = true, NetworkEndpointCount = 0,
                    HostWritableMountCount = 0, RepositoryInputReadOnly = true, OfflineFeedReadOnly = true,
                    WasDestroyed = true, InspectedAtUtc = completed
                },
                Stages = [], Artifacts = []
            };
            var json = SandboxEvidenceManifestCodec.SerializeCanonical(manifest);
            return Task.FromResult(new SandboxExecutionResult
            {
                ExecutionId = request.ExecutionId, Status = SandboxExecutionStatus.Succeeded,
                ReasonCode = SandboxReasonCodes.Ready, SafeSummary = "Builder proposal passed.",
                CleanedUp = true, EvidenceManifest = manifest, EvidenceManifestJson = json,
                EvidenceManifestSha256 = SandboxCanonicalJson.Sha256(json)
            });
        }
    }

    private sealed class ControllableBranchObserver :
        IBuilderRepositoryBranchObserver
    {
        public string BranchName { get; set; } = "main";
        public string HeadCommit { get; set; } = new('a', 40);

        public Task<BuilderRepositoryBranchObservation> ObserveAsync(
            string canonicalRepositoryPath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new BuilderRepositoryBranchObservation(
                BranchName,
                HeadCommit));
    }

    private sealed class AdjustableTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow =
            new(2026, 7, 23, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }

    private sealed class ControllableReadinessObserver : IRepositoryReadinessObserver
    {
        private readonly ISandboxSourceSnapshotBuilder _snapshots =
            new SandboxSourceSnapshotBuilder();

        public string GitTreeId { get; set; } = new('c', 40);

        public Task<RepositoryObservationResult> ObserveAsync(
            ObserveRepositoryStateRequest request,
            CancellationToken cancellationToken = default)
        {
            var fingerprint = request.ProvisioningManifestJson is not null &&
                              request.ProvisioningManifestSha256 is not null
                ? _snapshots.Describe(new SandboxSourceSnapshotRequest(
                    request.RepositoryBindingId,
                    request.CanonicalRepositoryPath,
                    request.BaselineCommit,
                    GitTreeId,
                    request.ProvisioningManifestJson,
                    request.ProvisioningManifestSha256,
                    request.CanonicalRepositoryPath)).WorktreeFingerprint
                : Hash(
                    $"builder-authorization-observation-v1\n" +
                    $"{request.RepositoryBindingId:D}\n" +
                    $"{request.RepositoryBindingRevision}\n" +
                    $"{request.BaselineCommit}");
            var observation = new RepositoryStateObservation
            {
                Id = Guid.NewGuid(),
                RepositoryBindingId = request.RepositoryBindingId,
                RepositoryBindingRevision = request.RepositoryBindingRevision,
                BaselineCommit = request.BaselineCommit,
                HeadCommit = request.BaselineCommit,
                GitTreeId = GitTreeId,
                WorktreeState = RepositoryWorktreeStates.Clean,
                WorktreeFingerprint = fingerprint,
                ObservedAtUtc = DateTimeOffset.UtcNow,
                EvidenceHash = new string('0', 64)
            };
            observation = observation with
            {
                EvidenceHash =
                    RepositoryStateObservationCodec.ComputeEvidenceHash(observation)
            };
            return Task.FromResult(new RepositoryObservationResult(
                RepositoryStateObservationCodec.NormalizeAndValidate(observation),
                [new CodeIndexSourceFingerprint(1, "src/App.cs", Hash("git-object-app"))]));
        }
    }

    private sealed class StableBuilderConfiguration :
        IBuilderStableConfigurationProvider
    {
        public Task<BuilderStableConfigurationBinding?> GetCurrentAsync(
            int tenantId,
            int projectId,
            CancellationToken cancellationToken = default)
        {
            var binding = new BuilderStableConfigurationBinding
            {
                ConfigurationId =
                    Guid.Parse("4c61f541-50ef-53c4-9fab-92aa8d272eb2"),
                Revision = 1,
                ProviderId = "test-provider",
                ModelId = "test-builder",
                ConfigurationSha256 = Hash("builder-authorization-configuration-v1")
            };
            return Task.FromResult<BuilderStableConfigurationBinding?>(
                BuilderStableConfigurationEvidenceCodec.NormalizeAndValidate(binding));
        }
    }

    private sealed class AvailableExecutionChecker : IExecutionAvailabilityChecker
    {
        public Task<ExecutionAvailabilityCheck> CheckAsync(
            ExecutionAvailabilityRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ExecutionAvailabilityCheck
            {
                State = ExecutionAvailabilityStates.Available,
                ReasonCode = "BuilderExecutionAvailable",
                SafeMessage = "Builder is available.",
                CheckedAtUtc = DateTimeOffset.UtcNow
            });
    }

    private sealed class PassingPolicyCatalog : ISandboxRuntimePolicyCatalog
    {
        public SandboxPolicyResolution Resolve(SandboxExecutionProfileBinding profile)
        {
            var digest = Hash("sandbox-image");
            var supervisorHash = Hash("trusted-supervisor");
            var offlineFeedHash = Hash("offline-feed");
            var policy = new SandboxRuntimePolicy
            {
                SchemaVersion = 1,
                PolicyVersion = SandboxPolicyVersions.WorkbenchV01,
                IsolationMode = SandboxIsolationModes.HcsHyperV,
                ProfileDefinitionId = profile.ProfileDefinitionId,
                ProfileDescriptorRevision = profile.ProfileDescriptorRevision,
                DescriptorSha256 = profile.DescriptorSha256,
                TemplateBundleSha256 = profile.TemplateBundleSha256,
                ToolchainManifestId = profile.ToolchainManifestId,
                ContainerImageReference = $"irondev.test/sdk@sha256:{digest}",
                ContainerImageDigest = digest,
                OfflineFeedPath = @"C:\IronDev.Test\offline-feed",
                OfflineFeedManifestSha256 = offlineFeedHash,
                RepositoryInputReadOnly = true,
                OfflineFeedReadOnly = true,
                TrustedSupervisorVersion = "builder-authorization-test-supervisor-v1",
                TrustedSupervisorSha256 = supervisorHash,
                Resources = SandboxResourcePolicy.WorkbenchV01,
                Restore = Command(
                    SandboxExecutionStage.Restore,
                    profile.RestoreCommand,
                    300),
                Build = Command(
                    SandboxExecutionStage.Build,
                    profile.BuildCommand,
                    300),
                Test = Command(
                    SandboxExecutionStage.Test,
                    profile.TestCommand,
                    600),
                EnvironmentAllowList = [],
                PolicySha256 = new string('0', 64)
            };
            policy = policy with
            {
                PolicySha256 = SandboxRuntimePolicyCodec.ComputeHash(policy)
            };
            return new SandboxPolicyResolution(
                new SandboxCapability(
                    SandboxCapabilityStates.Available,
                    SandboxReasonCodes.Ready,
                    "Test sandbox is available.",
                    policy.PolicyVersion,
                    policy.PolicySha256),
                policy);
        }

        private static SandboxCommandPolicy Command(
            SandboxExecutionStage stage,
            string text,
            int timeout) =>
            new(stage, text, SandboxCanonicalJson.Sha256(text), timeout);
    }

    private sealed class PassingSandboxExecutionService : ISandboxExecutionService
    {
        public Task<SandboxCapability> GetCapabilityAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new SandboxCapability(
                SandboxCapabilityStates.Available,
                SandboxReasonCodes.Ready,
                "Test sandbox is available.",
                SandboxPolicyVersions.WorkbenchV01,
                null));

        public Task<SandboxExecutionResult> ExecuteAsync(
            SandboxExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            var started = DateTimeOffset.UtcNow;
            var completed = started.AddMilliseconds(30);
            var manifest = new SandboxEvidenceManifest
            {
                SchemaVersion = 1,
                ExecutionId = request.ExecutionId,
                ProjectId = request.ProjectId,
                RepositoryBindingId = request.RepositoryBindingId,
                RepositoryBindingRevision = request.RepositoryBindingRevision,
                BaselineCommit = request.BaselineCommit,
                WorktreeFingerprint = request.WorktreeFingerprint,
                ProjectExecutionProfileId = request.ProjectExecutionProfileId,
                ProjectExecutionProfileRevision =
                    request.ProjectExecutionProfileRevision,
                ProfileDefinitionId = request.Policy.ProfileDefinitionId,
                ProfileDescriptorRevision =
                    request.Policy.ProfileDescriptorRevision,
                DescriptorSha256 = request.Policy.DescriptorSha256,
                TemplateBundleSha256 = request.Policy.TemplateBundleSha256,
                ToolchainManifestId = request.Policy.ToolchainManifestId,
                ContainerImageDigest = request.Policy.ContainerImageDigest,
                SandboxPolicyVersion = request.Policy.PolicyVersion,
                SandboxPolicySha256 = request.Policy.PolicySha256,
                TrustedSupervisorVersion =
                    request.Policy.TrustedSupervisorVersion,
                TrustedSupervisorSha256 =
                    request.Policy.TrustedSupervisorSha256,
                OfflineFeedManifestSha256 =
                    request.Policy.OfflineFeedManifestSha256,
                Status = SandboxExecutionStatus.Succeeded,
                ReasonCode = SandboxReasonCodes.Ready,
                SafeSummary =
                    "Restore, build, and test passed in the test sandbox.",
                StartedAtUtc = started,
                CompletedAtUtc = completed,
                Inspection = new SandboxRuntimeInspection
                {
                    RuntimeName = "builder-authorization-test-runtime",
                    IsolationMode = request.Policy.IsolationMode,
                    ActualContainerImageDigest =
                        request.Policy.ContainerImageDigest,
                    VirtualCpuCount = request.Policy.Resources.VirtualCpuCount,
                    MemoryMaximumMiB =
                        request.Policy.Resources.MemoryMaximumMiB,
                    WritableScratchMaximumGiB =
                        request.Policy.Resources.WritableScratchMaximumGiB,
                    MaximumUntrustedWorkloadProcessCount =
                        request.Policy.Resources.MaximumUntrustedWorkloadProcessCount,
                    UntrustedWorkloadProcessScope = "sandbox",
                    TrustedSupervisorVersion =
                        request.Policy.TrustedSupervisorVersion,
                    TrustedSupervisorSha256 =
                        request.Policy.TrustedSupervisorSha256,
                    SuspendedAssignmentBeforeResumeProven = true,
                    UntrustedWorkloadProcessLimitProven = true,
                    RestrictedLowIntegrityWorkloadIdentityProven = true,
                    SupervisorHandleIsolationProven = true,
                    WorkloadScratchAndEvidenceBoundaryProven = true,
                    BrokerLaunchDenialProven = true,
                    ProjectBytesCopiedAfterPreflightProven = true,
                    NetworkEndpointCount = 0,
                    HostWritableMountCount = 0,
                    RepositoryInputReadOnly = true,
                    OfflineFeedReadOnly = true,
                    WasDestroyed = true,
                    InspectedAtUtc = completed
                },
                Stages =
                [
                    Stage(request.Policy.Restore, 5),
                    Stage(request.Policy.Build, 10),
                    Stage(request.Policy.Test, 15)
                ],
                Artifacts = []
            };
            var json = SandboxEvidenceManifestCodec.SerializeCanonical(manifest);
            return Task.FromResult(new SandboxExecutionResult
            {
                ExecutionId = request.ExecutionId,
                Status = SandboxExecutionStatus.Succeeded,
                ReasonCode = SandboxReasonCodes.Ready,
                SafeSummary = manifest.SafeSummary,
                CleanedUp = true,
                EvidenceManifest = manifest,
                EvidenceManifestJson = json,
                EvidenceManifestSha256 = SandboxCanonicalJson.Sha256(json)
            });
        }

        public Task<SandboxExecutionResult?> TryRecoverCompletedAsync(
            SandboxCompletedEvidenceRecoveryRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<SandboxExecutionResult?>(null);

        public Task<SandboxExecutionCleanupResult> RecoverExecutionAsync(
            SandboxExecutionCleanupRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new SandboxExecutionCleanupResult(
                true,
                true,
                "Test cleanup complete."));

        public Task<SandboxRecoveryResult> RecoverAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new SandboxRecoveryResult(
                0,
                0,
                0,
                true,
                "No test sandbox recovery required."));

        private static SandboxStageEvidence Stage(
            SandboxCommandPolicy command,
            long duration) => new()
        {
            Stage = command.Stage,
            CommandSha256 = command.CommandSha256,
            ExitCode = 0,
            TimedOut = false,
            DurationMilliseconds = duration,
            StandardOutputSha256 = Hash($"{command.Stage}-stdout"),
            StandardErrorSha256 = Hash($"{command.Stage}-stderr"),
            StandardOutputTruncated = false,
            StandardErrorTruncated = false
        };
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private sealed record ReadyDeliveryFixture(
        int ProjectId,
        long WorkbenchSessionId,
        long LeaseEpoch,
        long BindingRevision,
        long ProfileRevision,
        IReadOnlyList<ProjectTicket> Tickets);

    private sealed record PreparedRunDatabaseRow(
        string Status,
        DateTime? ProviderInvokedAtUtc,
        string RoleContextJson,
        string ToolManifestJson,
        DateTime? ConsumedAtUtc,
        Guid? ConsumedByBuilderExecutionRunId);

    private sealed class Pr07AWriteCounts
    {
        public int Cores { get; init; }
        public int TicketReferences { get; init; }
        public int Authorizations { get; init; }
        public int CompletedOperations { get; init; }
        public int LegacyRuns { get; init; }
        public int BuilderRuns { get; init; }
        public int BuilderAttempts { get; init; }
    }
}
