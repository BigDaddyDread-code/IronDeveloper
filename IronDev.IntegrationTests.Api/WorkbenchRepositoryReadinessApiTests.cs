using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using IronDev.Core.Sandbox;
using IronDev.Core.Workbench;
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
public sealed class WorkbenchRepositoryReadinessApiTests : ApiTestBase
{
    private const string ProfileDefinitionId =
        RepositorySetupProfileIds.GreenfieldWinFormsNet10MstestV1;

    [TestMethod]
    public async Task QualifiedRepository_RequiresEvidence_ThenPublishesExactReadyRows_AndAvailabilityIsEphemeral()
    {
        using var scenario = new ReadinessScenario();
        using var factory = scenario.CreateFactory();
        using var client = await AuthenticatedClientAsync(factory);
        var fixture = await CreateQualifiedRepositoryAsync(client, "Readiness evidence", scenario.Observer);

        var before = await ReadContextAsync(client, fixture.ProjectId);
        Assert.AreEqual(ProjectExecutionReadinessStates.ValidationRequired,
            before.Evaluation.ExecutionReadiness);
        Assert.IsFalse(before.Evaluation.IsReady);

        await QualifySandboxAsync(client, fixture);
        var operationId = Guid.NewGuid();
        var ready = await RefreshAsync(client, fixture, operationId);
        Assert.AreEqual(ProjectExecutionReadinessStates.Ready,
            ready.Evaluation.ExecutionReadiness);
        Assert.IsTrue(ready.Evaluation.IsReady);
        Assert.AreEqual(9, ready.Evaluation.Gates.Count);
        Assert.IsTrue(ready.Evaluation.Gates.All(gate => gate.Passed));

        await using (var connection = new SqlConnection(ConnectionString))
        {
            var rows = await connection.QuerySingleAsync<ReadyRows>(
                """
                SELECT
                  (SELECT COUNT(1) FROM dbo.TechnicalValidationAttempts
                   WHERE TenantId=1 AND ProjectId=@ProjectId AND State=N'Passed') AS Attempts,
                  (SELECT COUNT(1) FROM dbo.RepositoryStateObservations
                   WHERE TenantId=1 AND ProjectId=@ProjectId) AS Observations,
                  (SELECT COUNT(1) FROM dbo.BuildValidationRecords
                   WHERE TenantId=1 AND ProjectId=@ProjectId) AS Builds,
                  (SELECT COUNT(1) FROM dbo.TestValidationRecords
                   WHERE TenantId=1 AND ProjectId=@ProjectId) AS Tests,
                  (SELECT COUNT(1) FROM dbo.CodeIndexSnapshots
                   WHERE TenantId=1 AND ProjectId=@ProjectId AND IndexState=N'Ready') AS Indexes,
                  (SELECT COUNT(1) FROM dbo.BuilderModelConfigurationRecords
                   WHERE TenantId=1 AND ProjectId=@ProjectId AND ConfigurationState=N'Configured') AS Builders,
                  (SELECT COUNT(1) FROM dbo.ProjectTechnicalReadinessEvidence
                   WHERE TenantId=1 AND ProjectId=@ProjectId) AS Companions,
                  (SELECT COUNT(1) FROM dbo.ProjectReadinessAssessments
                   WHERE TenantId=1 AND ProjectId=@ProjectId AND ExecutionReadiness=N'Ready') AS ReadyAssessments,
                  (SELECT COUNT(1) FROM dbo.WorkbenchOutboxEvents
                   WHERE TenantId=1 AND ProjectId=@ProjectId
                     AND EventKind=N'RepositoryTechnicalReadinessReady') AS ReadyOutbox,
                  (SELECT COUNT(1) FROM dbo.Runs WHERE ProjectId=@ProjectId) AS Runs,
                  (SELECT COUNT(1) FROM dbo.WorkbenchAgentRuns
                   WHERE TenantId=1 AND ProjectId=@ProjectId) AS BuilderRuns;
                """,
                new { fixture.ProjectId });
            Assert.AreEqual(1, rows.Attempts);
            Assert.AreEqual(1, rows.Observations);
            Assert.AreEqual(1, rows.Builds);
            Assert.AreEqual(1, rows.Tests);
            Assert.AreEqual(1, rows.Indexes);
            Assert.AreEqual(1, rows.Builders);
            Assert.AreEqual(1, rows.Companions);
            Assert.AreEqual(1, rows.ReadyAssessments);
            Assert.AreEqual(1, rows.ReadyOutbox);
            Assert.AreEqual(0, rows.Runs);
            Assert.AreEqual(0, rows.BuilderRuns);
        }

        scenario.Availability.Available = false;
        var unavailable = await ReadContextAsync(client, fixture.ProjectId);
        Assert.AreEqual(ProjectExecutionReadinessStates.Ready,
            unavailable.Evaluation.ExecutionReadiness,
            "A provider outage is display-only and must not mutate durable readiness.");
        Assert.IsNotNull(unavailable.Evaluation.Availability);
        Assert.IsFalse(unavailable.Evaluation.Availability.IsAvailable);

        scenario.Observer.WorktreeState = RepositoryWorktreeStates.Dirty;
        var dirty = await ReadContextAsync(client, fixture.ProjectId);
        Assert.AreEqual(ProjectExecutionReadinessStates.ValidationRequired,
            dirty.Evaluation.ExecutionReadiness);
        scenario.Observer.WorktreeState = RepositoryWorktreeStates.Clean;
        scenario.Observer.HeadCommitOverride = new string('d', 40);
        var baselineDrift = await ReadContextAsync(client, fixture.ProjectId);
        Assert.AreEqual(ProjectExecutionReadinessStates.ValidationRequired,
            baselineDrift.Evaluation.ExecutionReadiness);

        scenario.Observer.HeadCommitOverride = null;
        scenario.Builder.RotateConfiguration();
        var builderConfigurationDrift = await ReadContextAsync(client, fixture.ProjectId);
        Assert.AreEqual(ProjectExecutionReadinessStates.ValidationRequired,
            builderConfigurationDrift.Evaluation.ExecutionReadiness);
        Assert.AreEqual(RepositoryReadinessReasonCodes.BuilderModelConfigurationRequired,
            builderConfigurationDrift.Evaluation.ReasonCode);
        Assert.AreEqual(8, builderConfigurationDrift.Evaluation.Gates.Count(static gate => gate.Passed));
        var failedBuilderGate = builderConfigurationDrift.Evaluation.Gates.Single(static gate => !gate.Passed);
        Assert.AreEqual(RepositoryReadinessGateName.BuilderModelConfigured, failedBuilderGate.Gate);
        Assert.AreEqual(RepositoryReadinessReasonCodes.BuilderModelConfigurationRequired,
            failedBuilderGate.ReasonCode);

        var newerSandbox = await StartSandboxAsync(client, fixture);
        Assert.AreEqual(SandboxQualificationStates.Passed, newerSandbox.Attempt.State);
        var combinedDrift = await ReadContextAsync(client, fixture.ProjectId);
        Assert.AreEqual(ProjectExecutionReadinessStates.ValidationRequired,
            combinedDrift.Evaluation.ExecutionReadiness);
        Assert.AreEqual(RepositoryReadinessReasonCodes.SandboxQualificationRequired,
            combinedDrift.Evaluation.ReasonCode);
        Assert.AreEqual(7, combinedDrift.Evaluation.Gates.Count(static gate => gate.Passed));
        CollectionAssert.AreEquivalent(
            new[]
            {
                RepositoryReadinessGateName.SandboxQualified,
                RepositoryReadinessGateName.BuilderModelConfigured
            },
            combinedDrift.Evaluation.Gates
                .Where(static gate => !gate.Passed)
                .Select(static gate => gate.Gate)
                .ToArray());
    }

    [TestMethod]
    public async Task CompletedOperation_ReplaysAfterLeaseRenewal_ButRejectsRevisionMismatch()
    {
        using var scenario = new ReadinessScenario();
        using var factory = scenario.CreateFactory();
        using var client = await AuthenticatedClientAsync(factory);
        var fixture = await CreateQualifiedRepositoryAsync(client, "Readiness replay", scenario.Observer);
        await QualifySandboxAsync(client, fixture);
        var operationId = Guid.NewGuid();
        var first = await RefreshAsync(client, fixture, operationId);

        await using (var connection = new SqlConnection(ConnectionString))
        {
            await connection.ExecuteAsync(
                """
                UPDATE dbo.WorkbenchWriteLeases
                SET ExpiresAtUtc=DATEADD(MINUTE, -1, SYSUTCDATETIME())
                WHERE TenantId=1 AND ProjectId=@ProjectId
                  AND WorkbenchSessionId=@WorkbenchSessionId AND LeaseEpoch=@LeaseEpoch;
                """,
                fixture);
        }

        var open = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{fixture.ProjectId}/open",
            new { clientOperationId = Guid.NewGuid(), takeOver = true });
        Assert.AreEqual(HttpStatusCode.OK, open.StatusCode,
            await open.Content.ReadAsStringAsync());
        var renewed = await open.Content.ReadFromJsonAsync<WorkbenchProjectEntryContext>();
        Assert.IsNotNull(renewed);
        Assert.IsTrue(renewed.LeaseEpoch > fixture.LeaseEpoch);
        var current = fixture with
        {
            WorkbenchSessionId = renewed.WorkbenchSessionId,
            LeaseEpoch = renewed.LeaseEpoch
        };

        var replay = await RefreshAsync(client, current, operationId);
        Assert.IsTrue(replay.IsReplay);
        Assert.AreEqual(first.RepositoryStateObservationId, replay.RepositoryStateObservationId);
        Assert.AreEqual(first.BuildValidationRecordId, replay.BuildValidationRecordId);
        Assert.AreEqual(first.TestValidationRecordId, replay.TestValidationRecordId);
        Assert.AreEqual(first.CodeIndexSnapshotId, replay.CodeIndexSnapshotId);

        var mismatch = await client.PostAsJsonAsync(
            ReadinessValidationUrl(current.ProjectId),
            ReadinessPayload(
                current,
                operationId,
                bindingRevision: current.BindingRevision + 1));
        Assert.AreEqual(HttpStatusCode.Conflict, mismatch.StatusCode,
            await mismatch.Content.ReadAsStringAsync());
        var error = await mismatch.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.IsNotNull(error);
        Assert.AreEqual(RepositoryReadinessOperationMismatchException.ErrorCode, error.Error);
    }

    [TestMethod]
    public async Task FinalizationFailure_RollsBackCompanionAndSuccessOutbox_ThenRecordsBoundedFailure()
    {
        using var scenario = new ReadinessScenario();
        scenario.FailureInjector.FailurePoint =
            RepositoryReadinessRefreshFailurePoint.OutboxEventsCreated;
        using var factory = scenario.CreateFactory();
        using var client = await AuthenticatedClientAsync(factory);
        var fixture = await CreateQualifiedRepositoryAsync(client, "Readiness rollback", scenario.Observer);
        await QualifySandboxAsync(client, fixture);

        var response = await client.PostAsJsonAsync(
            ReadinessValidationUrl(fixture.ProjectId),
            ReadinessPayload(fixture, Guid.NewGuid()));
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode,
            await response.Content.ReadAsStringAsync());

        await using var connection = new SqlConnection(ConnectionString);
        var rows = await connection.QuerySingleAsync<FailureRows>(
            """
            SELECT
              (SELECT COUNT(1) FROM dbo.TechnicalValidationAttempts
               WHERE TenantId=1 AND ProjectId=@ProjectId AND State=N'Failed') AS FailedAttempts,
              (SELECT COUNT(1) FROM dbo.ProjectTechnicalReadinessEvidence
               WHERE TenantId=1 AND ProjectId=@ProjectId) AS Companions,
              (SELECT COUNT(1) FROM dbo.RepositoryStateObservations
               WHERE TenantId=1 AND ProjectId=@ProjectId) AS Observations,
              (SELECT COUNT(1) FROM dbo.BuildValidationRecords
               WHERE TenantId=1 AND ProjectId=@ProjectId) AS Builds,
              (SELECT COUNT(1) FROM dbo.TestValidationRecords
               WHERE TenantId=1 AND ProjectId=@ProjectId) AS Tests,
              (SELECT COUNT(1) FROM dbo.CodeIndexSnapshots
               WHERE TenantId=1 AND ProjectId=@ProjectId) AS Indexes,
              (SELECT COUNT(1) FROM dbo.BuilderModelConfigurationRecords
               WHERE TenantId=1 AND ProjectId=@ProjectId) AS Builders,
              (SELECT COUNT(1) FROM dbo.ProjectReadinessAssessments
               WHERE TenantId=1 AND ProjectId=@ProjectId AND ExecutionReadiness=N'Ready') AS ReadyAssessments,
              (SELECT COUNT(1) FROM dbo.WorkbenchOutboxEvents
               WHERE TenantId=1 AND ProjectId=@ProjectId
                 AND EventKind=N'RepositoryTechnicalReadinessReady') AS SuccessOutbox,
              (SELECT COUNT(1) FROM dbo.WorkbenchOutboxEvents
               WHERE TenantId=1 AND ProjectId=@ProjectId
                 AND EventKind=N'RepositoryTechnicalValidationFailed') AS FailureOutbox;
            """,
            new { fixture.ProjectId });
        Assert.AreEqual(1, rows.FailedAttempts);
        Assert.AreEqual(0, rows.Companions);
        Assert.AreEqual(0, rows.Observations);
        Assert.AreEqual(0, rows.Builds);
        Assert.AreEqual(0, rows.Tests);
        Assert.AreEqual(0, rows.Indexes);
        Assert.AreEqual(0, rows.Builders);
        Assert.AreEqual(0, rows.ReadyAssessments);
        Assert.AreEqual(0, rows.SuccessOutbox);
        Assert.AreEqual(1, rows.FailureOutbox);
    }

    [TestMethod]
    public async Task FinalObservationDrift_TerminalizesExactClaim_AndDoesNotBlockRetry()
    {
        using var scenario = new ReadinessScenario();
        scenario.Observer.DriftOnSecondObservation = true;
        using var factory = scenario.CreateFactory();
        using var client = await AuthenticatedClientAsync(factory);
        var fixture = await CreateQualifiedRepositoryAsync(client, "Readiness TOCTOU fence", scenario.Observer);
        await QualifySandboxAsync(client, fixture);

        var response = await client.PostAsJsonAsync(
            ReadinessValidationUrl(fixture.ProjectId),
            ReadinessPayload(fixture, Guid.NewGuid()));
        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode,
            await response.Content.ReadAsStringAsync());
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.IsNotNull(error);
        Assert.AreEqual(RepositoryReadinessStaleConfigurationException.ErrorCode, error.Error);

        await using (var connection = new SqlConnection(ConnectionString))
        {
            var rows = await connection.QuerySingleAsync<DriftFailureRows>(
                """
                SELECT
                  (SELECT COUNT(1) FROM dbo.TechnicalValidationAttempts
                   WHERE TenantId=1 AND ProjectId=@ProjectId AND State=N'Failed') AS FailedAttempts,
                  (SELECT COUNT(1) FROM dbo.TechnicalValidationAttempts
                   WHERE TenantId=1 AND ProjectId=@ProjectId AND State=N'Running') AS RunningAttempts,
                  (SELECT COUNT(1) FROM dbo.ClientOperations
                   WHERE TenantId=1 AND ResultProjectId=@ProjectId
                     AND OperationKind=N'ValidateRepositoryTechnicalReadiness'
                     AND Status=N'Failed') AS FailedOperations,
                  (SELECT COUNT(1) FROM dbo.ClientOperations
                   WHERE TenantId=1 AND ResultProjectId=@ProjectId
                     AND OperationKind=N'ValidateRepositoryTechnicalReadiness'
                     AND Status=N'Pending') AS PendingOperations,
                  (SELECT COUNT(1) FROM dbo.ProjectTechnicalReadinessEvidence
                   WHERE TenantId=1 AND ProjectId=@ProjectId) AS Companions;
                """,
                new { fixture.ProjectId });
            Assert.AreEqual(1, rows.FailedAttempts);
            Assert.AreEqual(0, rows.RunningAttempts);
            Assert.AreEqual(1, rows.FailedOperations);
            Assert.AreEqual(0, rows.PendingOperations);
            Assert.AreEqual(0, rows.Companions);
        }

        scenario.Observer.DriftOnSecondObservation = false;
        var retry = await RefreshAsync(client, fixture, Guid.NewGuid());
        Assert.AreEqual(ProjectExecutionReadinessStates.Ready,
            retry.Evaluation.ExecutionReadiness);
    }

    [TestMethod]
    public async Task NewerFailedAttempt_InvalidatesPriorReady_ForLiveAndSqlProjections()
    {
        using var scenario = new ReadinessScenario();
        using var factory = scenario.CreateFactory();
        using var client = await AuthenticatedClientAsync(factory);
        var fixture = await CreateQualifiedRepositoryAsync(client, "Failed refresh currentness", scenario.Observer);
        await QualifySandboxAsync(client, fixture);
        var ready = await RefreshAsync(client, fixture, Guid.NewGuid());
        Assert.AreEqual(ProjectExecutionReadinessStates.Ready,
            ready.Evaluation.ExecutionReadiness);

        scenario.FailureInjector.FailurePoint =
            RepositoryReadinessRefreshFailurePoint.OutboxEventsCreated;
        var failed = await client.PostAsJsonAsync(
            ReadinessValidationUrl(fixture.ProjectId),
            ReadinessPayload(fixture, Guid.NewGuid()));
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, failed.StatusCode,
            await failed.Content.ReadAsStringAsync());
        scenario.FailureInjector.FailurePoint = null;

        var live = await ReadContextAsync(client, fixture.ProjectId);
        Assert.AreEqual(ProjectExecutionReadinessStates.ValidationRequired,
            live.Evaluation.ExecutionReadiness,
            "The live service must not resurrect an older Ready operation after a newer failed attempt.");
        await using var connection = new SqlConnection(ConnectionString);
        var sqlProjection = await connection.QuerySingleAsync<string>(
            """
            SELECT ExecutionReadiness
            FROM dbo.vw_WorkbenchEffectiveProjectReadiness
            WHERE TenantId=1 AND ProjectId=@ProjectId;
            """,
            new { fixture.ProjectId });
        Assert.AreEqual(ProjectExecutionReadinessStates.ValidationRequired, sqlProjection);
    }

    [TestMethod]
    public async Task SandboxFingerprintMismatch_TerminalizesClaimWithoutMintingEvidence()
    {
        using var scenario = new ReadinessScenario();
        using var factory = scenario.CreateFactory();
        using var client = await AuthenticatedClientAsync(factory);
        var fixture = await CreateQualifiedRepositoryAsync(
            client,
            "Sandbox fingerprint mismatch",
            scenario.Observer);
        await QualifySandboxAsync(client, fixture);
        scenario.Observer.GitTreeIdOverride = new string('d', 40);

        var response = await client.PostAsJsonAsync(
            ReadinessValidationUrl(fixture.ProjectId),
            ReadinessPayload(fixture, Guid.NewGuid()));
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode,
            await response.Content.ReadAsStringAsync());
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.IsNotNull(error);
        Assert.AreEqual(RepositoryReadinessIntegrityException.ErrorCode, error.Error);

        await using var connection = new SqlConnection(ConnectionString);
        var terminal = await connection.QuerySingleAsync<DriftFailureRows>(
            """
            SELECT
              (SELECT COUNT(1) FROM dbo.TechnicalValidationAttempts
               WHERE TenantId=1 AND ProjectId=@ProjectId AND State=N'Failed') AS FailedAttempts,
              (SELECT COUNT(1) FROM dbo.TechnicalValidationAttempts
               WHERE TenantId=1 AND ProjectId=@ProjectId AND State=N'Running') AS RunningAttempts,
              (SELECT COUNT(1) FROM dbo.ClientOperations
               WHERE TenantId=1 AND ResultProjectId=@ProjectId
                 AND OperationKind=N'ValidateRepositoryTechnicalReadiness'
                 AND Status=N'Failed') AS FailedOperations,
              (SELECT COUNT(1) FROM dbo.ClientOperations
               WHERE TenantId=1 AND ResultProjectId=@ProjectId
                 AND OperationKind=N'ValidateRepositoryTechnicalReadiness'
                 AND Status=N'Pending') AS PendingOperations,
              (SELECT COUNT(1) FROM dbo.ProjectTechnicalReadinessEvidence
               WHERE TenantId=1 AND ProjectId=@ProjectId) AS Companions;
            """,
            new { fixture.ProjectId });
        Assert.AreEqual(1, terminal.FailedAttempts);
        Assert.AreEqual(0, terminal.RunningAttempts);
        Assert.AreEqual(1, terminal.FailedOperations);
        Assert.AreEqual(0, terminal.PendingOperations);
        Assert.AreEqual(0, terminal.Companions);

        var children = await connection.QuerySingleAsync<FailureRows>(
            """
            SELECT
              (SELECT COUNT(1) FROM dbo.RepositoryStateObservations
               WHERE TenantId=1 AND ProjectId=@ProjectId) AS Observations,
              (SELECT COUNT(1) FROM dbo.BuildValidationRecords
               WHERE TenantId=1 AND ProjectId=@ProjectId) AS Builds,
              (SELECT COUNT(1) FROM dbo.TestValidationRecords
               WHERE TenantId=1 AND ProjectId=@ProjectId) AS Tests,
              (SELECT COUNT(1) FROM dbo.CodeIndexSnapshots
               WHERE TenantId=1 AND ProjectId=@ProjectId) AS Indexes;
            """,
            new { fixture.ProjectId });
        Assert.AreEqual(0, children.Observations);
        Assert.AreEqual(0, children.Builds);
        Assert.AreEqual(0, children.Tests);
        Assert.AreEqual(0, children.Indexes);
    }

    [TestMethod]
    public async Task NewerFailedSandboxAttempt_InvalidatesOlderPass_ForLiveRefreshAndSqlProjection()
    {
        using var scenario = new ReadinessScenario();
        using var factory = scenario.CreateFactory();
        using var client = await AuthenticatedClientAsync(factory);
        var fixture = await CreateQualifiedRepositoryAsync(
            client,
            "Failed sandbox currentness",
            scenario.Observer);
        await QualifySandboxAsync(client, fixture);
        var ready = await RefreshAsync(client, fixture, Guid.NewGuid());
        Assert.AreEqual(ProjectExecutionReadinessStates.Ready, ready.Evaluation.ExecutionReadiness);

        scenario.Sandbox.FailNext = true;
        var failedSandbox = await StartSandboxAsync(client, fixture);
        Assert.AreEqual(SandboxQualificationStates.Failed, failedSandbox.Attempt.State);

        var live = await ReadContextAsync(client, fixture.ProjectId);
        Assert.AreEqual(ProjectExecutionReadinessStates.ValidationRequired,
            live.Evaluation.ExecutionReadiness);
        Assert.AreEqual(RepositoryReadinessReasonCodes.SandboxQualificationRequired,
            live.Evaluation.ReasonCode);
        Assert.IsFalse(live.Evaluation.Gates.Single(
            gate => gate.Gate == RepositoryReadinessGateName.SandboxQualified).Passed);
        Assert.IsTrue(live.Evaluation.Gates.Where(
            gate => gate.Gate != RepositoryReadinessGateName.SandboxQualified).All(gate => gate.Passed));
        var refresh = await client.PostAsJsonAsync(
            ReadinessValidationUrl(fixture.ProjectId),
            ReadinessPayload(fixture, Guid.NewGuid()));
        Assert.AreEqual(HttpStatusCode.Conflict, refresh.StatusCode,
            await refresh.Content.ReadAsStringAsync());
        var error = await refresh.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.IsNotNull(error);
        Assert.AreEqual(RepositoryReadinessNotAllowedException.ErrorCode, error.Error);

        await using var connection = new SqlConnection(ConnectionString);
        var currentness = await connection.QuerySingleAsync<SandboxCurrentnessRows>(
            """
            SELECT
              (SELECT TOP (1) State FROM dbo.SandboxQualificationAttempts
               WHERE TenantId=1 AND ProjectId=@ProjectId
               ORDER BY AttemptNumber DESC, StartedAtUtc DESC) AS LatestSandboxState,
              (SELECT COUNT(1) FROM dbo.SandboxQualificationAttempts
               WHERE TenantId=1 AND ProjectId=@ProjectId AND State=N'Passed') AS PassedSandboxAttempts,
              (SELECT COUNT(1) FROM dbo.SandboxQualificationAttempts
               WHERE TenantId=1 AND ProjectId=@ProjectId AND State=N'Failed') AS FailedSandboxAttempts,
              (SELECT ExecutionReadiness FROM dbo.vw_WorkbenchEffectiveProjectReadiness
               WHERE TenantId=1 AND ProjectId=@ProjectId) AS ExecutionReadiness;
            """,
            new { fixture.ProjectId });
        Assert.AreEqual(SandboxQualificationStates.Failed, currentness.LatestSandboxState);
        Assert.AreEqual(1, currentness.PassedSandboxAttempts);
        Assert.AreEqual(1, currentness.FailedSandboxAttempts);
        Assert.AreEqual(ProjectExecutionReadinessStates.ValidationRequired,
            currentness.ExecutionReadiness);
    }

    [TestMethod]
    public async Task NewerPassedSandboxAttempt_InvalidatesOlderTechnicalEvidence_InLiveAndSqlProjections()
    {
        using var scenario = new ReadinessScenario();
        using var factory = scenario.CreateFactory();
        using var client = await AuthenticatedClientAsync(factory);
        var fixture = await CreateQualifiedRepositoryAsync(
            client,
            "New passed sandbox currentness",
            scenario.Observer);
        var firstSandbox = await StartSandboxAsync(client, fixture);
        Assert.AreEqual(SandboxQualificationStates.Passed, firstSandbox.Attempt.State);
        var ready = await RefreshAsync(client, fixture, Guid.NewGuid());
        Assert.AreEqual(ProjectExecutionReadinessStates.Ready, ready.Evaluation.ExecutionReadiness);

        var secondSandbox = await StartSandboxAsync(client, fixture);
        Assert.AreEqual(SandboxQualificationStates.Passed, secondSandbox.Attempt.State);
        Assert.AreNotEqual(firstSandbox.Attempt.AttemptId, secondSandbox.Attempt.AttemptId);

        var live = await ReadContextAsync(client, fixture.ProjectId);
        Assert.AreEqual(ProjectExecutionReadinessStates.ValidationRequired,
            live.Evaluation.ExecutionReadiness);
        Assert.AreEqual(RepositoryReadinessReasonCodes.SandboxQualificationRequired,
            live.Evaluation.ReasonCode);
        Assert.IsFalse(live.Evaluation.Gates.Single(
            gate => gate.Gate == RepositoryReadinessGateName.SandboxQualified).Passed);
        Assert.IsTrue(live.Evaluation.Gates.Where(
            gate => gate.Gate != RepositoryReadinessGateName.SandboxQualified).All(gate => gate.Passed));

        await using var connection = new SqlConnection(ConnectionString);
        var sql = await connection.QuerySingleAsync<EffectiveReadinessRows>(
            """
            SELECT ExecutionReadiness, ReasonCode
            FROM dbo.vw_WorkbenchEffectiveProjectReadiness
            WHERE TenantId=1 AND ProjectId=@ProjectId;
            """,
            new { fixture.ProjectId });
        Assert.AreEqual(ProjectExecutionReadinessStates.ValidationRequired, sql.ExecutionReadiness);
        Assert.AreEqual(RepositoryReadinessReasonCodes.SandboxQualificationRequired, sql.ReasonCode);
    }

    [TestMethod]
    public async Task MissingStableBuilderConfiguration_PreservesEightPassingGatesAndExactReason()
    {
        using var scenario = new ReadinessScenario();
        scenario.Builder.Configured = false;
        using var factory = scenario.CreateFactory();
        using var client = await AuthenticatedClientAsync(factory);
        var fixture = await CreateQualifiedRepositoryAsync(
            client,
            "Missing stable Builder configuration",
            scenario.Observer);
        await QualifySandboxAsync(client, fixture);

        var completed = await RefreshAsync(client, fixture, Guid.NewGuid());
        Assert.AreEqual(ProjectExecutionReadinessStates.ValidationRequired,
            completed.Evaluation.ExecutionReadiness);
        Assert.AreEqual(RepositoryReadinessReasonCodes.BuilderModelConfigurationRequired,
            completed.Evaluation.ReasonCode);
        Assert.AreEqual(8, completed.Evaluation.Gates.Count(gate => gate.Passed));

        var live = await ReadContextAsync(client, fixture.ProjectId);
        Assert.AreEqual(ProjectExecutionReadinessStates.ValidationRequired,
            live.Evaluation.ExecutionReadiness);
        Assert.AreEqual(RepositoryReadinessReasonCodes.BuilderModelConfigurationRequired,
            live.Evaluation.ReasonCode);
        Assert.AreEqual(8, live.Evaluation.Gates.Count(gate => gate.Passed));
        Assert.IsNull(live.Evaluation.Availability);

        await using var connection = new SqlConnection(ConnectionString);
        var sql = await connection.QuerySingleAsync<EffectiveReadinessRows>(
            """
            SELECT viewState.ExecutionReadiness, viewState.ReasonCode
            FROM dbo.vw_WorkbenchEffectiveProjectReadiness viewState
            WHERE viewState.TenantId=1 AND viewState.ProjectId=@ProjectId;
            """,
            new { fixture.ProjectId });
        var attemptState = await connection.QuerySingleAsync<string>(
            """
            SELECT TOP (1) State
            FROM dbo.TechnicalValidationAttempts
            WHERE TenantId=1 AND ProjectId=@ProjectId
            ORDER BY AttemptNumber DESC;
            """,
            new { fixture.ProjectId });
        Assert.AreEqual(ProjectExecutionReadinessStates.ValidationRequired, sql.ExecutionReadiness);
        Assert.AreEqual(RepositoryReadinessReasonCodes.BuilderModelConfigurationRequired, sql.ReasonCode);
        Assert.AreEqual("Passed", attemptState,
            "A completed evidence collection is not a failed attempt merely because one readiness gate is unsatisfied.");
    }

    [TestMethod]
    public async Task StalePendingRecovery_IsTerminalizedAndDoesNotStrandTheProject()
    {
        using var scenario = new ReadinessScenario();
        using var factory = scenario.CreateFactory();
        using var client = await AuthenticatedClientAsync(factory);
        var fixture = await CreateQualifiedRepositoryAsync(
            client,
            "Stale pending readiness recovery",
            scenario.Observer);
        await QualifySandboxAsync(client, fixture);
        await RefreshAsync(client, fixture, Guid.NewGuid());

        var pendingOperationId = Guid.NewGuid();
        await SeedRunningReadinessAttemptAsync(fixture, pendingOperationId);
        var newerSandbox = await StartSandboxAsync(client, fixture);
        Assert.AreEqual(SandboxQualificationStates.Passed, newerSandbox.Attempt.State);

        var stale = await client.PostAsJsonAsync(
            ReadinessValidationUrl(fixture.ProjectId),
            ReadinessPayload(fixture, pendingOperationId));
        Assert.AreEqual(HttpStatusCode.Conflict, stale.StatusCode,
            await stale.Content.ReadAsStringAsync());
        var staleError = await stale.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.IsNotNull(staleError);
        Assert.AreEqual(RepositoryReadinessStaleConfigurationException.ErrorCode, staleError.Error);

        await using (var connection = new SqlConnection(ConnectionString))
        {
            var rows = await connection.QuerySingleAsync<DriftFailureRows>(
                """
                SELECT
                  (SELECT COUNT(1) FROM dbo.TechnicalValidationAttempts
                   WHERE TenantId=1 AND ProjectId=@ProjectId AND State=N'Failed') AS FailedAttempts,
                  (SELECT COUNT(1) FROM dbo.TechnicalValidationAttempts
                   WHERE TenantId=1 AND ProjectId=@ProjectId AND State=N'Running') AS RunningAttempts,
                  (SELECT COUNT(1) FROM dbo.ClientOperations
                   WHERE TenantId=1 AND ResultProjectId=@ProjectId
                     AND OperationKind=N'ValidateRepositoryTechnicalReadiness'
                     AND Status=N'Failed' AND ClientOperationId=@ClientOperationId) AS FailedOperations,
                  (SELECT COUNT(1) FROM dbo.ClientOperations
                   WHERE TenantId=1 AND ResultProjectId=@ProjectId
                     AND OperationKind=N'ValidateRepositoryTechnicalReadiness'
                     AND Status=N'Pending') AS PendingOperations,
                  (SELECT COUNT(1) FROM dbo.ProjectTechnicalReadinessEvidence
                   WHERE TenantId=1 AND ProjectId=@ProjectId) AS Companions;
                """,
                new { fixture.ProjectId, ClientOperationId = pendingOperationId });
            Assert.AreEqual(1, rows.FailedAttempts);
            Assert.AreEqual(0, rows.RunningAttempts);
            Assert.AreEqual(1, rows.FailedOperations);
            Assert.AreEqual(0, rows.PendingOperations);
        }

        var retry = await RefreshAsync(client, fixture, Guid.NewGuid());
        Assert.AreEqual(ProjectExecutionReadinessStates.Ready, retry.Evaluation.ExecutionReadiness);
    }

    [TestMethod]
    public async Task AmbiguousCommittedCompletion_ReplaysSuccessWithoutContradictoryFailureEvidence()
    {
        using var scenario = new ReadinessScenario();
        scenario.FailureInjector.FailurePoint =
            RepositoryReadinessRefreshFailurePoint.CompletionCommitted;
        using var factory = scenario.CreateFactory();
        using var client = await AuthenticatedClientAsync(factory);
        var fixture = await CreateQualifiedRepositoryAsync(
            client,
            "Ambiguous committed readiness completion",
            scenario.Observer);
        await QualifySandboxAsync(client, fixture);
        var operationId = Guid.NewGuid();

        var recovered = await RefreshAsync(client, fixture, operationId);
        Assert.IsTrue(recovered.IsReplay);
        Assert.AreEqual(ProjectExecutionReadinessStates.Ready, recovered.Evaluation.ExecutionReadiness);
        scenario.FailureInjector.FailurePoint = null;
        var replay = await RefreshAsync(client, fixture, operationId);
        Assert.IsTrue(replay.IsReplay);
        Assert.AreEqual(recovered.RepositoryStateObservationId, replay.RepositoryStateObservationId);

        await using var connection = new SqlConnection(ConnectionString);
        var events = await connection.QuerySingleAsync<AmbiguousCompletionRows>(
            """
            SELECT
              (SELECT COUNT(1) FROM dbo.TechnicalValidationAttempts
               WHERE TenantId=1 AND ProjectId=@ProjectId AND State=N'Passed') AS PassedAttempts,
              (SELECT COUNT(1) FROM dbo.TechnicalValidationAttempts
               WHERE TenantId=1 AND ProjectId=@ProjectId AND State=N'Failed') AS FailedAttempts,
              (SELECT COUNT(1) FROM dbo.ClientOperations
               WHERE TenantId=1 AND ResultProjectId=@ProjectId
                 AND ClientOperationId=@ClientOperationId AND Status=N'Completed') AS CompletedOperations,
              (SELECT COUNT(1) FROM dbo.WorkbenchOutboxEvents
               WHERE TenantId=1 AND ProjectId=@ProjectId
                 AND EventKind=N'RepositoryTechnicalReadinessReady') AS ReadyEvents,
              (SELECT COUNT(1) FROM dbo.WorkbenchOutboxEvents
               WHERE TenantId=1 AND ProjectId=@ProjectId
                 AND EventKind=N'RepositoryTechnicalValidationFailed') AS FailureEvents;
            """,
            new { fixture.ProjectId, ClientOperationId = operationId });
        Assert.AreEqual(1, events.PassedAttempts);
        Assert.AreEqual(0, events.FailedAttempts);
        Assert.AreEqual(1, events.CompletedOperations);
        Assert.AreEqual(1, events.ReadyEvents);
        Assert.AreEqual(0, events.FailureEvents);
    }

    private static async Task<QualifiedFixture> CreateQualifiedRepositoryAsync(
        HttpClient client,
        string name,
        ControllableReadinessObserver observer)
    {
        var startResponse = await client.PostAsJsonAsync(
            "/api/projects/start",
            new { clientOperationId = Guid.NewGuid(), name });
        Assert.AreEqual(HttpStatusCode.Created, startResponse.StatusCode,
            await startResponse.Content.ReadAsStringAsync());
        var start = await startResponse.Content.ReadFromJsonAsync<WorkbenchProjectEntryContext>();
        Assert.IsNotNull(start);

        var planResponse = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{start.ProjectId}/repository/setup-plans",
            new
            {
                start.WorkbenchSessionId,
                start.LeaseEpoch,
                profileDefinitionId = ProfileDefinitionId
            });
        Assert.AreEqual(HttpStatusCode.OK, planResponse.StatusCode,
            await planResponse.Content.ReadAsStringAsync());
        var plan = await planResponse.Content.ReadFromJsonAsync<RepositorySetupPlanPreview>();
        Assert.IsNotNull(plan);

        var confirmResponse = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{start.ProjectId}/repository/setup-confirmations",
            new
            {
                start.WorkbenchSessionId,
                start.LeaseEpoch,
                clientOperationId = Guid.NewGuid(),
                expectedPlanHash = plan.PlanHash
            });
        Assert.AreEqual(HttpStatusCode.OK, confirmResponse.StatusCode,
            await confirmResponse.Content.ReadAsStringAsync());
        var confirmation = await confirmResponse.Content
            .ReadFromJsonAsync<RepositorySetupConfirmationResult>();
        Assert.IsNotNull(confirmation);

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
        Assert.AreEqual(HttpStatusCode.OK, provisionResponse.StatusCode,
            await provisionResponse.Content.ReadAsStringAsync());
        var provisioned = await provisionResponse.Content
            .ReadFromJsonAsync<RepositoryProvisioningResult>();
        Assert.IsNotNull(provisioned);
        observer.GitTreeId = provisioned.GitTreeId;
        return new QualifiedFixture(
            start.ProjectId,
            start.WorkbenchSessionId,
            start.LeaseEpoch,
            provisioned.RepositoryBinding.Revision,
            provisioned.ExecutionProfile.Revision);
    }

    private static async Task QualifySandboxAsync(HttpClient client, QualifiedFixture fixture)
    {
        var result = await StartSandboxAsync(client, fixture);
        Assert.AreEqual(SandboxQualificationStates.Passed, result.Attempt.State);
    }

    private static async Task<WorkbenchSandboxQualificationResult> StartSandboxAsync(
        HttpClient client,
        QualifiedFixture fixture)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{fixture.ProjectId}/repository/sandbox-qualifications",
            new
            {
                fixture.WorkbenchSessionId,
                fixture.LeaseEpoch,
                clientOperationId = Guid.NewGuid(),
                expectedRepositoryBindingRevision = fixture.BindingRevision,
                expectedExecutionProfileRevision = fixture.ProfileRevision
            });
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
            await response.Content.ReadAsStringAsync());
        var result = await response.Content
            .ReadFromJsonAsync<WorkbenchSandboxQualificationResult>();
        Assert.IsNotNull(result);
        return result;
    }

    private static async Task<RefreshRepositoryReadinessResult> RefreshAsync(
        HttpClient client,
        QualifiedFixture fixture,
        Guid operationId)
    {
        var response = await client.PostAsJsonAsync(
            ReadinessValidationUrl(fixture.ProjectId),
            ReadinessPayload(fixture, operationId));
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
            await response.Content.ReadAsStringAsync());
        var result = await response.Content
            .ReadFromJsonAsync<RefreshRepositoryReadinessResult>();
        Assert.IsNotNull(result);
        return result;
    }

    private static async Task SeedRunningReadinessAttemptAsync(
        QualifiedFixture fixture,
        Guid clientOperationId)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        var operationRecordId = await connection.QuerySingleAsync<long>(
            """
            INSERT dbo.ClientOperations
                (TenantId, ActorUserId, OperationKind, ResourceScopeId,
                 ClientOperationId, PayloadHash, Status, ResultProjectId,
                 ResultWorkbenchSessionId)
            OUTPUT inserted.Id
            SELECT TOP (1)
                   TenantId, ActorUserId, OperationKind, ResourceScopeId,
                   @ClientOperationId, PayloadHash, N'Pending', ResultProjectId,
                   @WorkbenchSessionId
            FROM dbo.ClientOperations
            WHERE TenantId=1 AND ResultProjectId=@ProjectId
              AND OperationKind=N'ValidateRepositoryTechnicalReadiness'
              AND Status=N'Completed'
            ORDER BY Id DESC;
            """,
            new
            {
                fixture.ProjectId,
                fixture.WorkbenchSessionId,
                ClientOperationId = clientOperationId
            });
        var attemptId = Guid.NewGuid();
        var inserted = await connection.ExecuteAsync(
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
            SELECT TOP (1)
                 @AttemptId, source.TenantId, source.ProjectId, @OperationRecordId,
                 @ClientOperationId, source.ActorUserId, source.ClientOperationKind,
                 source.ClientOperationResourceScopeId, @WorkbenchSessionId, @LeaseEpoch,
                 (SELECT MAX(numbered.AttemptNumber) + 1
                  FROM dbo.TechnicalValidationAttempts numbered
                  WHERE numbered.TenantId=source.TenantId AND numbered.ProjectId=source.ProjectId
                    AND numbered.RepositoryBindingId=source.RepositoryBindingId),
                 source.RepositoryBindingId, source.RepositoryBindingRevision, source.BaselineCommit,
                 source.ProjectExecutionProfileId, source.ProjectExecutionProfileRevision,
                 source.ProfileDefinitionId, source.ProfileDescriptorRevision,
                 source.ProfileDescriptorSha256, source.RestoreCommandSha256,
                 source.BuildCommandSha256, source.TestCommandSha256,
                 source.ToolchainManifestId, source.ToolchainManifestSha256,
                 source.ContainerImageDigest, source.ContainerImageDigestSha256,
                 source.SandboxPolicyVersion, source.SandboxPolicySha256,
                 source.OfflineFeedManifestSha256, source.TemplateBundleSha256,
                 source.SandboxQualificationAttemptId, source.SandboxEvidenceManifestId,
                 source.SandboxEvidenceManifestSha256, N'Running', SYSUTCDATETIME()
            FROM dbo.TechnicalValidationAttempts source
            WHERE source.TenantId=1 AND source.ProjectId=@ProjectId AND source.State=N'Passed'
            ORDER BY source.AttemptNumber DESC;
            """,
            new
            {
                AttemptId = attemptId,
                OperationRecordId = operationRecordId,
                ClientOperationId = clientOperationId,
                fixture.ProjectId,
                fixture.WorkbenchSessionId,
                fixture.LeaseEpoch
            });
        Assert.AreEqual(1, inserted);
    }

    private static async Task<WorkbenchRepositoryReadinessContext> ReadContextAsync(
        HttpClient client,
        int projectId)
    {
        var response = await client.GetAsync(
            $"/api/workbench/projects/{projectId}/repository/readiness");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
            await response.Content.ReadAsStringAsync());
        var result = await response.Content
            .ReadFromJsonAsync<WorkbenchRepositoryReadinessContext>();
        Assert.IsNotNull(result);
        return result;
    }

    private static object ReadinessPayload(
        QualifiedFixture fixture,
        Guid operationId,
        long? bindingRevision = null) => new
    {
        fixture.WorkbenchSessionId,
        fixture.LeaseEpoch,
        clientOperationId = operationId,
        expectedRepositoryBindingRevision = bindingRevision ?? fixture.BindingRevision,
        expectedExecutionProfileRevision = fixture.ProfileRevision
    };

    private static string ReadinessValidationUrl(int projectId) =>
        $"/api/workbench/projects/{projectId}/repository/readiness-validations";

    private static async Task<HttpClient> AuthenticatedClientAsync(
        WebApplicationFactory<Program> factory)
    {
        var token = await SelectTenantAsync(await LoginAsync());
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private sealed class ReadinessScenario : IDisposable
    {
        private readonly string _root = CreateRoot();
        public ControllableReadinessObserver Observer { get; } = new();
        public ControllableBuilderConfiguration Builder { get; } = new();
        public ControllableAvailability Availability { get; } = new();
        public ControllableFailureInjector FailureInjector { get; } = new();
        public PassingSandboxExecutionService Sandbox { get; } = new();

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
                builder.UseSetting("WorkbenchRepositorySetup:ApprovedWorkspaceRoot", repositoryRoot);
                builder.UseSetting("WorkbenchRepositoryProvisioning:GitExecutable", "git");
                builder.UseSetting("WorkbenchRepositoryProvisioning:GitTimeoutSeconds", "30");
                builder.UseSetting("WorkbenchProductionSandbox:SourceSnapshotRoot", snapshotRoot);
                builder.UseSetting("WorkbenchProductionSandbox:EvidenceRoot", evidenceRoot);
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<ISandboxRuntimePolicyCatalog>();
                    services.AddSingleton<ISandboxRuntimePolicyCatalog, PassingPolicyCatalog>();
                    services.RemoveAll<ISandboxExecutionService>();
                    services.AddSingleton<ISandboxExecutionService>(Sandbox);
                    services.RemoveAll<IRepositoryReadinessObserver>();
                    services.AddSingleton<IRepositoryReadinessObserver>(Observer);
                    services.RemoveAll<IBuilderStableConfigurationProvider>();
                    services.AddSingleton<IBuilderStableConfigurationProvider>(Builder);
                    services.RemoveAll<IExecutionAvailabilityChecker>();
                    services.AddSingleton<IExecutionAvailabilityChecker>(Availability);
                    services.RemoveAll<IRepositoryReadinessRefreshFailureInjector>();
                    services.AddSingleton<IRepositoryReadinessRefreshFailureInjector>(FailureInjector);
                });
            });
        }

        public void Dispose() => DeleteRoot(_root);

        private static string CreateRoot()
        {
            var drive = Path.GetPathRoot(Environment.SystemDirectory)
                        ?? throw new InvalidOperationException("A system drive is required.");
            var root = Path.Combine(
                drive,
                "IronDev.RepositoryReadiness.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        private static void DeleteRoot(string root)
        {
            if (!Directory.Exists(root)) return;
            var drive = Path.GetPathRoot(Environment.SystemDirectory)!;
            var safeBase = Path.GetFullPath(Path.Combine(
                drive,
                "IronDev.RepositoryReadiness.Tests"));
            var exact = Path.GetFullPath(root);
            if (!exact.StartsWith(safeBase + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(exact, safeBase, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Refusing to remove a non-test readiness root.");
            if (File.GetAttributes(exact).HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidOperationException("Refusing to remove a reparse-point test root.");
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

    private sealed class ControllableReadinessObserver : IRepositoryReadinessObserver
    {
        private readonly ISandboxSourceSnapshotBuilder _snapshots = new SandboxSourceSnapshotBuilder();
        private int _observationCount;
        public string WorktreeState { get; set; } = RepositoryWorktreeStates.Clean;
        public string? HeadCommitOverride { get; set; }
        public string GitTreeId { get; set; } = new('c', 40);
        public string? GitTreeIdOverride { get; set; }
        public bool DriftOnSecondObservation { get; set; }

        public Task<RepositoryObservationResult> ObserveAsync(
            ObserveRepositoryStateRequest request,
            CancellationToken cancellationToken = default)
        {
            var observationNumber = Interlocked.Increment(ref _observationCount);
            var worktreeState = DriftOnSecondObservation && observationNumber == 2
                ? RepositoryWorktreeStates.Dirty
                : WorktreeState;
            var head = HeadCommitOverride ?? request.BaselineCommit;
            var gitTreeId = GitTreeIdOverride ?? GitTreeId;
            var fingerprint = request.ProvisioningManifestJson is not null &&
                              request.ProvisioningManifestSha256 is not null
                ? _snapshots.Describe(new SandboxSourceSnapshotRequest(
                    request.RepositoryBindingId,
                    request.CanonicalRepositoryPath,
                    request.BaselineCommit,
                    gitTreeId,
                    request.ProvisioningManifestJson,
                    request.ProvisioningManifestSha256,
                    request.CanonicalRepositoryPath)).WorktreeFingerprint
                : Hash(
                    $"readiness-observation-v1\n{request.RepositoryBindingId:D}\n" +
                    $"{request.RepositoryBindingRevision}\n{request.BaselineCommit}\n{head}\n{worktreeState}");
            var observation = new RepositoryStateObservation
            {
                Id = Guid.NewGuid(),
                RepositoryBindingId = request.RepositoryBindingId,
                RepositoryBindingRevision = request.RepositoryBindingRevision,
                BaselineCommit = request.BaselineCommit,
                HeadCommit = head,
                GitTreeId = gitTreeId,
                WorktreeState = worktreeState,
                WorktreeFingerprint = fingerprint,
                ObservedAtUtc = DateTimeOffset.UtcNow,
                EvidenceHash = new string('0', 64)
            };
            observation = observation with
            {
                EvidenceHash = RepositoryStateObservationCodec.ComputeEvidenceHash(observation)
            };
            return Task.FromResult(new RepositoryObservationResult(
                RepositoryStateObservationCodec.NormalizeAndValidate(observation),
                [new CodeIndexSourceFingerprint(1, "src/App.cs", Hash("git-object-app"))]));
        }
    }

    private sealed class ControllableBuilderConfiguration : IBuilderStableConfigurationProvider
    {
        private int _generation = 1;
        public bool Configured { get; set; } = true;

        public void RotateConfiguration() => _generation++;

        public Task<BuilderStableConfigurationBinding?> GetCurrentAsync(
            int tenantId,
            int projectId,
            CancellationToken cancellationToken = default)
        {
            if (!Configured)
                return Task.FromResult<BuilderStableConfigurationBinding?>(null);
            var hash = Hash($"builder-configuration-v1\n{_generation}");
            return Task.FromResult<BuilderStableConfigurationBinding?>(
                BuilderStableConfigurationEvidenceCodec.NormalizeAndValidate(
                    new BuilderStableConfigurationBinding
                    {
                        ConfigurationId = _generation == 1
                            ? Guid.Parse("4c61f541-50ef-53c4-9fab-92aa8d272eb2")
                            : Guid.Parse("73eb1441-8c13-5aea-8d73-222324446ef4"),
                        Revision = _generation,
                        ProviderId = "test-provider",
                        ModelId = "test-builder",
                        ConfigurationSha256 = hash
                    }));
        }
    }

    private sealed class ControllableAvailability : IExecutionAvailabilityChecker
    {
        public bool Available { get; set; } = true;

        public Task<ExecutionAvailabilityCheck> CheckAsync(
            ExecutionAvailabilityRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(new ExecutionAvailabilityCheck
        {
            State = Available
                ? ExecutionAvailabilityStates.Available
                : ExecutionAvailabilityStates.Unavailable,
            ReasonCode = Available ? "BuilderExecutionAvailable" : "ProviderUnavailable",
            SafeMessage = Available
                ? "Builder is available."
                : "Builder provider is unavailable.",
            CheckedAtUtc = DateTimeOffset.UtcNow
        });
    }

    private sealed class ControllableFailureInjector : IRepositoryReadinessRefreshFailureInjector
    {
        public RepositoryReadinessRefreshFailurePoint? FailurePoint { get; set; }

        public void ThrowIfRequested(RepositoryReadinessRefreshFailurePoint point)
        {
            if (FailurePoint == point)
                throw new InvalidOperationException($"Injected readiness failure at {point}.");
        }
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
                TrustedSupervisorVersion = "readiness-test-supervisor-v1",
                TrustedSupervisorSha256 = supervisorHash,
                Resources = SandboxResourcePolicy.WorkbenchV01,
                Restore = Command(SandboxExecutionStage.Restore, profile.RestoreCommand, 300),
                Build = Command(SandboxExecutionStage.Build, profile.BuildCommand, 300),
                Test = Command(SandboxExecutionStage.Test, profile.TestCommand, 600),
                EnvironmentAllowList = [],
                PolicySha256 = new string('0', 64)
            };
            policy = policy with { PolicySha256 = SandboxRuntimePolicyCodec.ComputeHash(policy) };
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
            int timeout) => new(stage, text, SandboxCanonicalJson.Sha256(text), timeout);
    }

    private sealed class PassingSandboxExecutionService : ISandboxExecutionService
    {
        public bool FailNext { get; set; }

        public Task<SandboxCapability> GetCapabilityAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(new SandboxCapability(
            SandboxCapabilityStates.Available,
            SandboxReasonCodes.Ready,
            "Test sandbox is available.",
            SandboxPolicyVersions.WorkbenchV01,
            null));

        public Task<SandboxExecutionResult> ExecuteAsync(
            SandboxExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            if (FailNext)
            {
                FailNext = false;
                throw new InvalidOperationException("Injected sandbox execution failure.");
            }
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
                ProjectExecutionProfileRevision = request.ProjectExecutionProfileRevision,
                ProfileDefinitionId = request.Policy.ProfileDefinitionId,
                ProfileDescriptorRevision = request.Policy.ProfileDescriptorRevision,
                DescriptorSha256 = request.Policy.DescriptorSha256,
                TemplateBundleSha256 = request.Policy.TemplateBundleSha256,
                ToolchainManifestId = request.Policy.ToolchainManifestId,
                ContainerImageDigest = request.Policy.ContainerImageDigest,
                SandboxPolicyVersion = request.Policy.PolicyVersion,
                SandboxPolicySha256 = request.Policy.PolicySha256,
                TrustedSupervisorVersion = request.Policy.TrustedSupervisorVersion,
                TrustedSupervisorSha256 = request.Policy.TrustedSupervisorSha256,
                OfflineFeedManifestSha256 = request.Policy.OfflineFeedManifestSha256,
                Status = SandboxExecutionStatus.Succeeded,
                ReasonCode = SandboxReasonCodes.Ready,
                SafeSummary = "Restore, build, and test passed in the test sandbox.",
                StartedAtUtc = started,
                CompletedAtUtc = completed,
                Inspection = new SandboxRuntimeInspection
                {
                    RuntimeName = "readiness-test-runtime",
                    IsolationMode = request.Policy.IsolationMode,
                    ActualContainerImageDigest = request.Policy.ContainerImageDigest,
                    VirtualCpuCount = request.Policy.Resources.VirtualCpuCount,
                    MemoryMaximumMiB = request.Policy.Resources.MemoryMaximumMiB,
                    WritableScratchMaximumGiB = request.Policy.Resources.WritableScratchMaximumGiB,
                    MaximumUntrustedWorkloadProcessCount =
                        request.Policy.Resources.MaximumUntrustedWorkloadProcessCount,
                    UntrustedWorkloadProcessScope = "sandbox",
                    TrustedSupervisorVersion = request.Policy.TrustedSupervisorVersion,
                    TrustedSupervisorSha256 = request.Policy.TrustedSupervisorSha256,
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
            CancellationToken cancellationToken = default) => Task.FromResult(
            new SandboxExecutionCleanupResult(true, true, "Test cleanup complete."));

        public Task<SandboxRecoveryResult> RecoverAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(
            new SandboxRecoveryResult(0, 0, 0, true, "No test sandbox recovery required."));

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

    private sealed record QualifiedFixture(
        int ProjectId,
        long WorkbenchSessionId,
        long LeaseEpoch,
        long BindingRevision,
        long ProfileRevision);

    private sealed record ErrorResponse(string Error, string Message);

    private sealed class ReadyRows
    {
        public int Attempts { get; init; }
        public int Observations { get; init; }
        public int Builds { get; init; }
        public int Tests { get; init; }
        public int Indexes { get; init; }
        public int Builders { get; init; }
        public int Companions { get; init; }
        public int ReadyAssessments { get; init; }
        public int ReadyOutbox { get; init; }
        public int Runs { get; init; }
        public int BuilderRuns { get; init; }
    }

    private sealed class FailureRows
    {
        public int FailedAttempts { get; init; }
        public int Companions { get; init; }
        public int Observations { get; init; }
        public int Builds { get; init; }
        public int Tests { get; init; }
        public int Indexes { get; init; }
        public int Builders { get; init; }
        public int ReadyAssessments { get; init; }
        public int SuccessOutbox { get; init; }
        public int FailureOutbox { get; init; }
    }

    private sealed class DriftFailureRows
    {
        public int FailedAttempts { get; init; }
        public int RunningAttempts { get; init; }
        public int FailedOperations { get; init; }
        public int PendingOperations { get; init; }
        public int Companions { get; init; }
    }

    private sealed class SandboxCurrentnessRows
    {
        public string LatestSandboxState { get; init; } = string.Empty;
        public int PassedSandboxAttempts { get; init; }
        public int FailedSandboxAttempts { get; init; }
        public string ExecutionReadiness { get; init; } = string.Empty;
    }

    private sealed class EffectiveReadinessRows
    {
        public string ExecutionReadiness { get; init; } = string.Empty;
        public string ReasonCode { get; init; } = string.Empty;
    }

    private sealed class AmbiguousCompletionRows
    {
        public int PassedAttempts { get; init; }
        public int FailedAttempts { get; init; }
        public int CompletedOperations { get; init; }
        public int ReadyEvents { get; init; }
        public int FailureEvents { get; init; }
    }
}
