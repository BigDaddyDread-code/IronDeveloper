using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using IronDev.Api.Controllers;
using IronDev.Core.Workbench;
using IronDev.Infrastructure.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api;

[TestClass]
public sealed class WorkbenchRepositoryProvisioningApiTests : ApiTestBase
{
    private const string ProfileId = RepositorySetupProfileIds.GreenfieldWinFormsNet10MstestV1;

    [TestMethod]
    public void ProvisioningRequest_ContainsOnlyExactServerAuthorityReferences()
    {
        var requestType = typeof(WorkbenchRepositoryController).GetNestedType(
            "ProvisionRepositoryRequest",
            BindingFlags.Public | BindingFlags.NonPublic);
        Assert.IsNotNull(requestType, "The product controller must expose one typed provisioning request.");

        var properties = requestType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        CollectionAssert.AreEqual(
            new[]
            {
                "ClientOperationId",
                "ExpectedExecutionProfileRevision",
                "ExpectedRepositoryBindingRevision",
                "LeaseEpoch",
                "SetupConfirmationId",
                "WorkbenchSessionId"
            },
            properties);

        var joined = string.Join('|', properties);
        foreach (var forbidden in new[]
                 {
                     "Path", "Root", "Directory", "Template", "Content", "Command", "Git",
                     "Branch", "Cleanup", "Product", "Technology", "Restore", "Build", "Test",
                     "Index", "Sandbox", "Builder"
                 })
            Assert.IsFalse(joined.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                $"The browser provisioning request must not carry {forbidden} authority.");
    }

    [TestMethod]
    public async Task Provision_AtomicallyPublishesQualifiedProductNeutralRepositories_WithoutTechnicalExecution()
    {
        var root = CreateApprovedRoot();
        try
        {
            var unrelated = Path.Combine(root, "unrelated-owned-by-someone-else");
            Directory.CreateDirectory(unrelated);
            await File.WriteAllTextAsync(Path.Combine(unrelated, "keep.txt"), "do not touch\n");

            using var factory = ProvisioningFactory(root);
            using var client = await AuthenticatedClientAsync(factory);
            var first = await ConfirmSetupAsync(client, "Personal budget notebook");
            var second = await ConfirmSetupAsync(client, "Garden watering planner");
            Assert.IsFalse(Directory.Exists(first.Plan.TargetPath));
            Assert.IsFalse(Directory.Exists(second.Plan.TargetPath));

            var firstResult = await ProvisionAsync(client, first, Guid.NewGuid());
            var secondResult = await ProvisionAsync(client, second, Guid.NewGuid());

            await AssertQualifiedRepositoryAsync(first, firstResult);
            await AssertQualifiedRepositoryAsync(second, secondResult);
            Assert.IsTrue(File.Exists(Path.Combine(unrelated, "keep.txt")),
                "Atomic publication must not disturb an unrelated sibling.");

            var firstShape = NormalizedTemplateShape(first.Plan);
            var secondShape = NormalizedTemplateShape(second.Plan);
            CollectionAssert.AreEqual(firstShape.Keys.ToArray(), secondShape.Keys.ToArray());
            foreach (var key in firstShape.Keys)
                Assert.AreEqual(firstShape[key], secondShape[key],
                    $"Pinned template shape drifted for '{key}'.");

            await using var connection = new SqlConnection(ConnectionString);
            var firstState = await ReadStateAsync(connection, first.Project.ProjectId);
            AssertQualifiedState(first, firstResult, firstState);
            var secondState = await ReadStateAsync(connection, second.Project.ProjectId);
            AssertQualifiedState(second, secondResult, secondState);

            var beforeRerun = await ReadMigrationCountsAsync(connection);
            await connection.ExecuteAsync(
                "DISABLE TRIGGER dbo.TR_RepositoryProvisioningReceipts_AppendOnly " +
                "ON dbo.RepositoryProvisioningReceipts;");
            Assert.IsTrue(await connection.QuerySingleAsync<bool>(
                """
                SELECT CONVERT(BIT, is_disabled) FROM sys.triggers
                WHERE parent_id=OBJECT_ID(N'dbo.RepositoryProvisioningReceipts')
                  AND name=N'TR_RepositoryProvisioningReceipts_AppendOnly';
                """));
            await ApplyProvisioningMigrationAsync(connection);
            Assert.IsFalse(await connection.QuerySingleAsync<bool>(
                """
                SELECT CONVERT(BIT, is_disabled) FROM sys.triggers
                WHERE parent_id=OBJECT_ID(N'dbo.RepositoryProvisioningReceipts')
                  AND name=N'TR_RepositoryProvisioningReceipts_AppendOnly';
                """),
                "A migration restart must re-enable the append-only trigger even when " +
                "SetupConfirmationId is already NOT NULL.");
            var afterRerun = await ReadMigrationCountsAsync(connection);
            Assert.AreEqual(beforeRerun, afterRerun,
                "A provisioning migration rerun must preserve confirmed setup, attempts, and receipts.");

            await Assert.ThrowsExceptionAsync<SqlException>(() => connection.ExecuteAsync(
                "UPDATE dbo.RepositoryProvisioningReceipts SET BranchName=N'other' WHERE Id=@Id;",
                new { Id = firstResult.ReceiptId }));
            await Assert.ThrowsExceptionAsync<SqlException>(() => connection.ExecuteAsync(
                """
                UPDATE dbo.UserMutationAttribution SET Phase=N'Failed'
                WHERE Route=N'/api/workbench/projects/{projectId}/repository/provisionings'
                  AND CorrelationId=@CorrelationId;
                """,
                new { CorrelationId = firstResult.ClientOperationId.ToString("D") }));
        }
        finally
        {
            DeleteApprovedRoot(root);
        }
    }

    [TestMethod]
    public async Task Open_NeverResumesALeaseWhoseWorkbenchSessionIsHistorical()
    {
        var root = CreateApprovedRoot();
        try
        {
            using var factory = ProvisioningFactory(root);
            using var client = await AuthenticatedClientAsync(factory);
            var setup = await ConfirmSetupAsync(client, "Historical session fence refresh");
            await using (var connection = new SqlConnection(ConnectionString))
            {
                await connection.ExecuteAsync(
                    """
                    UPDATE dbo.WorkbenchSessions
                    SET Status=N'Historical', ClosedAtUtc=SYSUTCDATETIME()
                    WHERE TenantId=1 AND ProjectId=@ProjectId AND Id=@WorkbenchSessionId;
                    """,
                    setup.Project);
            }

            var refreshed = await RefreshFenceAsync(client, setup.Project.ProjectId);
            Assert.AreNotEqual(setup.Project.WorkbenchSessionId, refreshed.WorkbenchSessionId);
            Assert.IsTrue(refreshed.LeaseEpoch > setup.Project.LeaseEpoch);

            await using var verify = new SqlConnection(ConnectionString);
            var fences = await verify.QuerySingleAsync<HistoricalFenceState>(
                """
                SELECT
                    (SELECT Status FROM dbo.WorkbenchSessions
                     WHERE Id=@OldSessionId) AS OldSessionStatus,
                    (SELECT RevokedAtUtc FROM dbo.WorkbenchWriteLeases
                     WHERE TenantId=1 AND ProjectId=@ProjectId
                       AND WorkbenchSessionId=@OldSessionId AND LeaseEpoch=@OldEpoch) AS OldLeaseRevokedAtUtc,
                    (SELECT Status FROM dbo.WorkbenchSessions
                     WHERE Id=@NewSessionId) AS NewSessionStatus,
                    (SELECT COUNT(1) FROM dbo.WorkbenchWriteLeases
                     WHERE TenantId=1 AND ProjectId=@ProjectId
                       AND WorkbenchSessionId=@NewSessionId AND LeaseEpoch=@NewEpoch
                       AND HolderActorUserId=1 AND RevokedAtUtc IS NULL
                       AND ExpiresAtUtc>SYSUTCDATETIME()) AS CurrentLeaseRows;
                """,
                new
                {
                    setup.Project.ProjectId,
                    OldSessionId = setup.Project.WorkbenchSessionId,
                    OldEpoch = setup.Project.LeaseEpoch,
                    NewSessionId = refreshed.WorkbenchSessionId,
                    NewEpoch = refreshed.LeaseEpoch
                });
            Assert.AreEqual("Historical", fences.OldSessionStatus);
            Assert.IsNotNull(fences.OldLeaseRevokedAtUtc);
            Assert.AreEqual("Active", fences.NewSessionStatus);
            Assert.AreEqual(1, fences.CurrentLeaseRows);
        }
        finally
        {
            DeleteApprovedRoot(root);
        }
    }

    private static void AssertQualifiedState(
        ConfirmedSetup setup,
        RepositoryProvisioningResult result,
        ProvisioningState state)
    {
        Assert.AreEqual(1, state.Attempts);
        Assert.AreEqual(1, state.QualifiedAttempts);
        Assert.AreEqual(0, state.FailedAttempts);
        Assert.AreEqual(1, state.Receipts);
        Assert.AreEqual(1, state.CompletedOperations);
        Assert.AreEqual(1, state.AttemptedAttributions);
        Assert.AreEqual(1, state.CompletedAttributions);
        Assert.AreEqual(0, state.FailedAttributions);
        Assert.AreEqual(RepositoryBindingStates.Qualified, state.BindingState);
        Assert.IsTrue(state.BindingRevision >= 3,
            "SetupConfirmed -> Provisioning -> Qualified must be revisioned.");
        Assert.AreEqual("main", state.DefaultBranch);
        Assert.AreEqual(result.BaselineCommit, state.BaselineCommit);
        Assert.AreEqual(setup.Plan.TargetPath, state.LocalPath);
        Assert.AreEqual(ProjectExecutionReadinessStates.NotConfigured, state.ExecutionReadiness);
        Assert.AreEqual(ProjectLifecyclePhases.Shaping, state.LifecyclePhase);
        Assert.AreEqual(0, state.ProjectFiles);
        Assert.AreEqual(0, state.CodeIndexEntries);
        Assert.AreEqual(0, state.ProjectCommands);
        Assert.AreEqual(0, state.ProjectProfiles,
            "Provisioning must not grant the legacy Builder apply permission.");
        Assert.AreEqual(0, state.Runs,
            "PR-05B must not restore, build, test, sandbox, or execute Builder work.");
    }

    private static async Task AssertQualifiedRepositoryAsync(
        ConfirmedSetup setup,
        RepositoryProvisioningResult result)
    {
        Assert.AreEqual(setup.Project.ProjectId, result.ProjectId);
        Assert.AreNotEqual(Guid.Empty, result.AttemptId);
        Assert.AreNotEqual(Guid.Empty, result.ReceiptId);
        Assert.IsFalse(result.IsReplay);
        Assert.AreEqual(ProjectLifecyclePhases.Shaping, result.ProjectLifecyclePhase);
        Assert.AreEqual(ProjectExecutionReadinessStates.NotConfigured, result.ExecutionReadiness);
        Assert.AreEqual(RepositoryBindingStates.Qualified, result.RepositoryBinding.BindingState);
        Assert.AreEqual("main", result.RepositoryBinding.DefaultBranch);
        Assert.AreEqual("main", result.BranchName);
        Assert.AreEqual(result.BaselineCommit, result.RepositoryBinding.BaselineCommit);
        AssertLowerHex(result.BaselineCommit, 40);
        AssertLowerHex(result.GitTreeId, 40);
        AssertLowerHex(result.ManifestSha256, 64);

        var target = setup.Plan.TargetPath;
        Assert.IsTrue(Directory.Exists(target));
        Assert.IsTrue(Directory.Exists(Path.Combine(target, ".git")));
        Assert.AreEqual("main", await GitAsync(target, "rev-parse", "--abbrev-ref", "HEAD"));
        Assert.AreEqual(result.BaselineCommit, await GitAsync(target, "rev-parse", "HEAD"));
        Assert.AreEqual(result.GitTreeId, await GitAsync(target, "rev-parse", "HEAD^{tree}"));
        Assert.AreEqual(string.Empty,
            await GitAsync(target, "status", "--porcelain=v1", "--untracked-files=all"));

        var commit = await GitAsync(target, "log", "-1", "--format=%B");
        StringAssert.Contains(commit, $"IronDev-Plan-Hash: {setup.Plan.PlanHash}");
        StringAssert.Contains(commit, $"IronDev-Provisioning-Attempt: {result.AttemptId:D}");
        StringAssert.Contains(commit, $"IronDev-Manifest-Sha256: {result.ManifestSha256}");

        var catalog = new RepositorySetupProfileCatalog();
        var profile = catalog.Find(
            ProfileId,
            setup.Plan.ProfileDescriptorRevision,
            setup.Plan.ProfileDescriptorSha256);
        Assert.IsNotNull(profile);
        var rendered = RepositorySetupTemplateBundleRenderer.Render(profile.TemplateBundle, setup.Plan);
        var tracked = (await GitAsync(target, "ls-files"))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var expected = rendered.Files.Select(file => file.RelativePath.Replace('\\', '/'))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        CollectionAssert.AreEqual(expected, tracked);

        var strictUtf8 = new UTF8Encoding(false, true);
        foreach (var file in rendered.Files)
        {
            var path = Path.Combine(target,
                file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            var bytes = await File.ReadAllBytesAsync(path);
            Assert.IsFalse(bytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()),
                $"'{file.RelativePath}' must not contain a UTF-8 BOM.");
            var text = strictUtf8.GetString(bytes);
            Assert.IsFalse(text.Contains('\r'), $"'{file.RelativePath}' must use LF only.");
            Assert.IsTrue(text.EndsWith('\n'), $"'{file.RelativePath}' must have a final LF.");
            Assert.AreEqual(file.Utf8Content, text);
        }

        var forbiddenDirectories = Directory.EnumerateDirectories(target, "*", SearchOption.AllDirectories)
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Where(name => name!.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                           name.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
                           name.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||
                           name.Equals("coverage", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.AreEqual(0, forbiddenDirectories.Length);
    }

    private static SortedDictionary<string, string> NormalizedTemplateShape(
        RepositorySetupPlanPreview plan)
    {
        var profile = new RepositorySetupProfileCatalog().Find(
            ProfileId,
            plan.ProfileDescriptorRevision,
            plan.ProfileDescriptorSha256);
        Assert.IsNotNull(profile);
        var rendered = RepositorySetupTemplateBundleRenderer.Render(profile.TemplateBundle, plan);
        var replacements = new[]
        {
            (plan.TestProjectName, "{{TEST_PROJECT_NAME}}"),
            (plan.AppProjectName.ToLowerInvariant(), "{{APP_PROJECT_NAME_LOWER}}"),
            (plan.AppProjectName, "{{APP_PROJECT_NAME}}"),
            (plan.SolutionName, "{{SOLUTION_NAME}}")
        };
        string Normalize(string value)
        {
            foreach (var (actual, token) in replacements.OrderByDescending(value => value.Item1.Length))
                value = value.Replace(actual, token, StringComparison.OrdinalIgnoreCase);
            return value;
        }
        return new SortedDictionary<string, string>(
            rendered.Files.ToDictionary(
                file => Normalize(file.RelativePath.Replace('\\', '/')),
                file => Normalize(file.Utf8Content),
                StringComparer.Ordinal),
            StringComparer.Ordinal);
    }

    [TestMethod]
    public async Task Provision_PrePublishFailuresRecordEvidenceCleanOnlyOwnedStaging_AndRetrySafely()
    {
        var root = CreateApprovedRoot();
        var injector = new TestProvisioningFailureInjector();
        var gitRecorder = new RecordingProvisioningGitRunner();
        try
        {
            var sentinel = Path.Combine(root, "unrelated-sentinel");
            Directory.CreateDirectory(sentinel);
            await File.WriteAllTextAsync(Path.Combine(sentinel, "keep.txt"), "keep\n");
            using var factory = ProvisioningFactory(root, injector, gitRecorder);
            using var client = await AuthenticatedClientAsync(factory);

            var failurePoints = new[]
            {
                RepositoryProvisioningFailurePoint.ClaimCommitted,
                RepositoryProvisioningFailurePoint.BeforeStagingCreate,
                RepositoryProvisioningFailurePoint.StagingCreated,
                RepositoryProvisioningFailurePoint.BundleRendered,
                RepositoryProvisioningFailurePoint.GitInitialized,
                RepositoryProvisioningFailurePoint.GitIndexCreated,
                RepositoryProvisioningFailurePoint.GitCommitted,
                RepositoryProvisioningFailurePoint.BeforePublish
            };

            foreach (var point in failurePoints)
            {
                var setup = await ConfirmSetupAsync(client, $"Failure injection {point}");
                var failedOperation = Guid.NewGuid();
                injector.FailurePoint = point;
                var response = await client.PostAsJsonAsync(
                    ProvisioningUrl(setup.Project.ProjectId),
                    ProvisionPayload(setup, failedOperation));
                Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode,
                    $"Unexpected status at {point}: {await response.Content.ReadAsStringAsync()}");
                await AssertProvisioningErrorAsync(
                    response,
                    RepositoryProvisioningExecutionException.ErrorCode,
                    RepositoryProvisioningFailureCodes.UnexpectedFailure);

                await using (var connection = new SqlConnection(ConnectionString))
                {
                    var failed = await ReadStateAsync(connection, setup.Project.ProjectId);
                    Assert.AreEqual(1, failed.Attempts);
                    Assert.AreEqual(1, failed.FailedAttempts);
                    Assert.AreEqual(0, failed.Receipts);
                    Assert.AreEqual(1, failed.FailedOperations);
                    Assert.AreEqual(1, failed.AttemptedAttributions);
                    Assert.AreEqual(0, failed.CompletedAttributions);
                    Assert.AreEqual(1, failed.FailedAttributions);
                    Assert.AreEqual(RepositoryBindingStates.ProvisioningFailed, failed.BindingState);
                    Assert.AreEqual(RepositoryProvisioningFailureCodes.UnexpectedFailure,
                        failed.LastFailureCode);
                    Assert.IsTrue(IsJson(failed.LastFailureEvidenceJson));
                    Assert.IsFalse(failed.LastFailureEvidenceJson!.Contains(
                        nameof(InjectedProvisioningFailureException),
                        StringComparison.Ordinal));
                    Assert.IsFalse(failed.LastFailureEvidenceJson.Contains(root,
                        StringComparison.OrdinalIgnoreCase),
                        "Durable failure evidence must not expose the user-machine workspace path.");
                    Assert.IsNotNull(failed.LastStagingPath);
                    Assert.IsFalse(Directory.Exists(failed.LastStagingPath),
                        $"Exact owned staging must be cleaned after a pre-publish failure at {point}.");
                    Assert.IsNull(failed.LocalPath);
                    Assert.AreEqual(ProjectExecutionReadinessStates.NotConfigured,
                        failed.ExecutionReadiness);
                }
                Assert.IsFalse(Directory.Exists(setup.Plan.TargetPath));
                Assert.IsTrue(File.Exists(Path.Combine(sentinel, "keep.txt")));

                injector.FailurePoint = null;
                var refreshed = await ReadRepositoryContextAsync(client, setup.Project.ProjectId);
                Assert.IsNotNull(refreshed.RepositoryBinding);
                Assert.IsNotNull(refreshed.ExecutionProfile);
                RepositoryProvisioningResult retried;
                try
                {
                    retried = await ProvisionAsync(
                        client,
                        setup,
                        Guid.NewGuid(),
                        refreshed.RepositoryBinding.Revision,
                        refreshed.ExecutionProfile.Revision);
                }
                catch (AssertFailedException exception)
                {
                    throw new AssertFailedException(
                        $"Safe new-operation retry failed after injected point {point}: " +
                        exception.Message + Environment.NewLine + gitRecorder.DescribeLastFailure(),
                        exception);
                }
                await AssertQualifiedRepositoryAsync(setup, retried);

                await using var recoveredConnection = new SqlConnection(ConnectionString);
                var recovered = await ReadStateAsync(recoveredConnection, setup.Project.ProjectId);
                Assert.AreEqual(2, recovered.Attempts);
                Assert.AreEqual(1, recovered.FailedAttempts);
                Assert.AreEqual(1, recovered.QualifiedAttempts);
                Assert.AreEqual(1, recovered.Receipts);
                Assert.AreEqual(RepositoryBindingStates.Qualified, recovered.BindingState);
                Assert.AreEqual(2, recovered.AttemptedAttributions);
                Assert.AreEqual(1, recovered.CompletedAttributions);
                Assert.AreEqual(1, recovered.FailedAttributions);
            }
        }
        finally
        {
            injector.FailurePoint = null;
            DeleteApprovedRoot(root);
        }
    }

    [TestMethod]
    public async Task ProvisioningSchema_RejectsCrossAuthorityAttemptAndReceiptProvenance()
    {
        var root = CreateApprovedRoot();
        var injector = new TestProvisioningFailureInjector
        {
            FailurePoint = RepositoryProvisioningFailurePoint.StagingCreated
        };
        try
        {
            using var factory = ProvisioningFactory(root, injector);
            using var client = await AuthenticatedClientAsync(factory);
            var source = await ConfirmSetupAsync(client, "SQL provenance source");
            var foreign = await ConfirmSetupAsync(client, "SQL provenance foreign authority");

            var failed = await client.PostAsJsonAsync(
                ProvisioningUrl(source.Project.ProjectId),
                ProvisionPayload(source, Guid.NewGuid()));
            Assert.AreEqual(HttpStatusCode.UnprocessableEntity, failed.StatusCode,
                await failed.Content.ReadAsStringAsync());
            injector.FailurePoint = null;

            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();
            var attemptAuthorityColumns = await ReadForeignKeyColumnsAsync(
                connection,
                "FK_RepositoryProvisioningAttempts_ConfirmationAuthority");
            Assert.AreEqual(
                "TenantId,ProjectId,SetupConfirmationId,RepositoryBindingId," +
                "ProjectExecutionProfileId,PlanHash",
                attemptAuthorityColumns);
            var receiptAuthorityColumns = await ReadForeignKeyColumnsAsync(
                connection,
                "FK_RepositoryProvisioningReceipts_AttemptAuthority");
            Assert.AreEqual(
                "TenantId,ProjectId,ProvisioningAttemptId,SetupConfirmationId," +
                "RepositoryBindingId,ProjectExecutionProfileId,PlanHash",
                receiptAuthorityColumns);
            var clientOperationAuthorityColumns = await ReadForeignKeyColumnsAsync(
                connection,
                "FK_RepositoryProvisioningAttempts_ClientOperationAuthority");
            Assert.AreEqual(
                "ClientOperationRecordId,TenantId,ProjectId,ClientOperationId,ActorUserId," +
                "ClientOperationKind,ClientOperationResourceScopeId,WorkbenchSessionId",
                clientOperationAuthorityColumns);
            Assert.AreEqual(
                "Id,TenantId,ResultProjectId,ResultRepositoryProvisioningAttemptId," +
                "ResultWorkbenchSessionId",
                await ReadForeignKeyColumnsAsync(
                    connection,
                    "FK_ClientOperations_RepositoryProvisioningAttemptAuthority"));
            Assert.AreEqual(
                "TenantId,ResultProjectId,ResultRepositoryProvisioningAttemptId," +
                "ResultRepositoryProvisioningReceiptId",
                await ReadForeignKeyColumnsAsync(
                    connection,
                    "FK_ClientOperations_RepositoryProvisioningReceiptAuthority"));

            var sourceAttemptId = await connection.QuerySingleAsync<Guid>(
                """
                SELECT Id FROM dbo.RepositoryProvisioningAttempts
                WHERE TenantId=1 AND ProjectId=@ProjectId;
                """,
                new { source.Project.ProjectId });
            var mismatchActorId = await connection.QuerySingleAsync<int>(
                """
                INSERT dbo.Users(Email, DisplayName, PasswordHash, IsActive)
                OUTPUT inserted.Id
                SELECT CONCAT(N'provenance-', CONVERT(NVARCHAR(36), NEWID()), N'@irondev.local'),
                       N'Provenance Mismatch', PasswordHash, 1
                FROM dbo.Users WHERE Id=1;
                """);

            const string insertAttempt =
                """
                DECLARE @ParentClientOperationId UNIQUEIDENTIFIER=NEWID();
                DECLARE @ChildClientOperationId UNIQUEIDENTIFIER=
                    CASE WHEN @MismatchClientOperationId=1 THEN NEWID() ELSE @ParentClientOperationId END;
                DECLARE @NewClientOperationRecordId BIGINT;
                INSERT dbo.ClientOperations
                    (TenantId, ActorUserId, OperationKind, ResourceScopeId,
                     ClientOperationId, PayloadHash, Status, ResultProjectId,
                     ResultWorkbenchSessionId)
                SELECT @ParentTenantId, @ParentActorUserId, @ParentOperationKind,
                       @ParentResourceScopeId,
                       @ParentClientOperationId, REPLICATE('a', 64), N'Pending', @ParentProjectId,
                       @ParentWorkbenchSessionId
                FROM dbo.RepositoryProvisioningAttempts
                WHERE Id=@SourceAttemptId;
                SET @NewClientOperationRecordId=CONVERT(BIGINT, SCOPE_IDENTITY());

                INSERT dbo.RepositoryProvisioningAttempts
                    (Id, TenantId, ProjectId, RepositoryBindingId, ProjectExecutionProfileId,
                     SetupConfirmationId, ClientOperationRecordId, ClientOperationId,
                     ActorUserId, ClientOperationKind, ClientOperationResourceScopeId,
                     WorkbenchSessionId, LeaseEpoch, AttemptNumber,
                     ExpectedBindingRevision, ExpectedExecutionProfileRevision,
                     PlanHash, DescriptorSha256, TemplateBundleSha256, PlanningBundleSha256,
                     CanonicalTargetPath, StagingPath, State, StartedAtUtc)
                SELECT NEWID(), TenantId, ProjectId, RepositoryBindingId, ProjectExecutionProfileId,
                       @SetupConfirmationId, @NewClientOperationRecordId, @ChildClientOperationId,
                       ActorUserId, N'ProvisionRepository',
                       N'project:' + CONVERT(NVARCHAR(20), ProjectId) + N':repository-provisioning',
                       WorkbenchSessionId, LeaseEpoch, AttemptNumber + 100,
                       ExpectedBindingRevision, ExpectedExecutionProfileRevision,
                       @PlanHash, DescriptorSha256, TemplateBundleSha256, PlanningBundleSha256,
                       CanonicalTargetPath, StagingPath + N'.forged', N'Provisioning', SYSUTCDATETIME()
                FROM dbo.RepositoryProvisioningAttempts
                WHERE Id=@SourceAttemptId;
                """;
            object AttemptParameters(
                Guid setupConfirmationId,
                string planHash,
                int parentTenantId = 1,
                int? parentProjectId = null,
                int parentActorUserId = 1,
                string parentOperationKind = "ProvisionRepository",
                string? parentResourceScopeId = null,
                long? parentWorkbenchSessionId = null,
                bool mismatchClientOperationId = false) => new
            {
                SourceAttemptId = sourceAttemptId,
                SetupConfirmationId = setupConfirmationId,
                PlanHash = planHash,
                ParentTenantId = parentTenantId,
                ParentProjectId = parentProjectId ?? source.Project.ProjectId,
                ParentActorUserId = parentActorUserId,
                ParentOperationKind = parentOperationKind,
                ParentResourceScopeId = parentResourceScopeId ??
                    $"project:{source.Project.ProjectId}:repository-provisioning",
                ParentWorkbenchSessionId = parentWorkbenchSessionId ??
                    source.Project.WorkbenchSessionId,
                MismatchClientOperationId = mismatchClientOperationId
            };
            await AssertSqlConstraintRejectedAsync(
                connection,
                insertAttempt,
                AttemptParameters(foreign.Confirmation.ConfirmationId, source.Plan.PlanHash),
                "FK_RepositoryProvisioningAttempts_ConfirmationAuthority");
            await AssertSqlConstraintRejectedAsync(
                connection,
                insertAttempt,
                AttemptParameters(source.Confirmation.ConfirmationId, new string('f', 64)),
                "FK_RepositoryProvisioningAttempts_ConfirmationAuthority");

            foreach (var mismatchedClientAuthority in new[]
                     {
                         AttemptParameters(
                             source.Confirmation.ConfirmationId,
                             source.Plan.PlanHash,
                             parentTenantId: 2),
                         AttemptParameters(
                             source.Confirmation.ConfirmationId,
                             source.Plan.PlanHash,
                             parentProjectId: foreign.Project.ProjectId),
                         AttemptParameters(
                             source.Confirmation.ConfirmationId,
                             source.Plan.PlanHash,
                             parentActorUserId: mismatchActorId),
                         AttemptParameters(
                             source.Confirmation.ConfirmationId,
                             source.Plan.PlanHash,
                             mismatchClientOperationId: true),
                         AttemptParameters(
                             source.Confirmation.ConfirmationId,
                             source.Plan.PlanHash,
                             parentOperationKind: "DifferentOperation"),
                         AttemptParameters(
                             source.Confirmation.ConfirmationId,
                             source.Plan.PlanHash,
                             parentResourceScopeId: $"project:{foreign.Project.ProjectId}:repository-provisioning"),
                         AttemptParameters(
                             source.Confirmation.ConfirmationId,
                             source.Plan.PlanHash,
                             parentWorkbenchSessionId: foreign.Project.WorkbenchSessionId)
                     })
            {
                await AssertSqlConstraintRejectedAsync(
                    connection,
                    insertAttempt,
                    mismatchedClientAuthority,
                    "FK_RepositoryProvisioningAttempts_ClientOperationAuthority");
            }

            const string insertReceipt =
                """
                INSERT dbo.RepositoryProvisioningReceipts
                    (Id, TenantId, ProjectId, RepositoryBindingId, ProjectExecutionProfileId,
                     ProvisioningAttemptId, SetupConfirmationId, ActorUserId, BranchName,
                     BaselineCommit, PlanHash, ManifestSha256, GitTreeId, ManifestJson,
                     ReceiptJson, ReceiptSha256, PublishedAtUtc, RecordedAtUtc)
                SELECT NEWID(), TenantId, ProjectId, RepositoryBindingId, ProjectExecutionProfileId,
                       Id, @SetupConfirmationId, ActorUserId, N'main', REPLICATE('a', 40),
                       @PlanHash, REPLICATE('b', 64), REPLICATE('c', 40), N'{}', N'{}',
                       REPLICATE('d', 64), SYSUTCDATETIME(), SYSUTCDATETIME()
                FROM dbo.RepositoryProvisioningAttempts
                WHERE Id=@SourceAttemptId;
                """;
            await AssertSqlConstraintRejectedAsync(
                connection,
                insertReceipt,
                new
                {
                    SourceAttemptId = sourceAttemptId,
                    SetupConfirmationId = foreign.Confirmation.ConfirmationId,
                    PlanHash = source.Plan.PlanHash
                },
                "FK_RepositoryProvisioningReceipts_AttemptAuthority");
            await AssertSqlConstraintRejectedAsync(
                connection,
                insertReceipt,
                new
                {
                    SourceAttemptId = sourceAttemptId,
                    SetupConfirmationId = source.Confirmation.ConfirmationId,
                    PlanHash = new string('e', 64)
                },
                "FK_RepositoryProvisioningReceipts_AttemptAuthority");

            var currentSourceContext = await ReadRepositoryContextAsync(
                client,
                source.Project.ProjectId);
            Assert.IsNotNull(currentSourceContext.RepositoryBinding);
            Assert.IsNotNull(currentSourceContext.ExecutionProfile);
            var sourceResult = await ProvisionAsync(
                client,
                source,
                Guid.NewGuid(),
                currentSourceContext.RepositoryBinding.Revision,
                currentSourceContext.ExecutionProfile.Revision);
            await AssertSqlConstraintRejectedAsync(
                connection,
                """
                UPDATE dbo.ClientOperations
                SET ResultRepositoryProvisioningAttemptId=@ResultAttemptId,
                    ResultRepositoryProvisioningReceiptId=NULL,
                    ResultWorkbenchSessionId=NULL
                WHERE Id=
                    (SELECT ClientOperationRecordId FROM dbo.RepositoryProvisioningAttempts
                     WHERE Id=@OperationAttemptId);
                """,
                new
                {
                    OperationAttemptId = sourceAttemptId,
                    ResultAttemptId = sourceAttemptId
                },
                "CK_ClientOperations_RepositoryProvisioningResultAuthority");
            const string crossWireOperationResults =
                """
                UPDATE dbo.ClientOperations
                SET ResultRepositoryProvisioningAttemptId=@ResultAttemptId,
                    ResultRepositoryProvisioningReceiptId=@ResultReceiptId
                WHERE Id=
                    (SELECT ClientOperationRecordId FROM dbo.RepositoryProvisioningAttempts
                     WHERE Id=@OperationAttemptId);
                """;
            await AssertSqlConstraintRejectedAsync(
                connection,
                crossWireOperationResults,
                new
                {
                    OperationAttemptId = sourceAttemptId,
                    ResultAttemptId = sourceResult.AttemptId,
                    ResultReceiptId = (Guid?)null
                },
                "FK_ClientOperations_RepositoryProvisioningAttemptAuthority");
            await AssertSqlConstraintRejectedAsync(
                connection,
                crossWireOperationResults,
                new
                {
                    OperationAttemptId = sourceAttemptId,
                    ResultAttemptId = sourceAttemptId,
                    ResultReceiptId = sourceResult.ReceiptId
                },
                "FK_ClientOperations_RepositoryProvisioningReceiptAuthority");

            var persisted = await ReadStateAsync(connection, source.Project.ProjectId);
            Assert.AreEqual(2, persisted.Attempts);
            Assert.AreEqual(1, persisted.FailedAttempts);
            Assert.AreEqual(1, persisted.QualifiedAttempts);
            Assert.AreEqual(1, persisted.Receipts);
        }
        finally
        {
            injector.FailurePoint = null;
            DeleteApprovedRoot(root);
        }
    }

    [TestMethod]
    public async Task Provision_PrePublishFailureCannotCommitFailedStateAfterItsFenceIsRevoked()
    {
        var root = CreateApprovedRoot();
        var injector = new TestProvisioningFailureInjector
        {
            FailurePoint = RepositoryProvisioningFailurePoint.StagingCreated
        };
        try
        {
            using var factory = ProvisioningFactory(root, injector);
            using var client = await AuthenticatedClientAsync(factory);
            var setup = await ConfirmSetupAsync(client, "Pre-publish failure fence revocation");
            var operationId = Guid.NewGuid();
            var revokedAtFailureBoundary = 0;
            injector.Callback = point =>
            {
                if (point != RepositoryProvisioningFailurePoint.StagingCreated ||
                    Interlocked.Exchange(ref revokedAtFailureBoundary, 1) != 0)
                    return;
                ApplyFinalizeFenceMutation(setup.Project, FinalizeFenceMutation.RevokedEpoch);
            };

            var fenced = await client.PostAsJsonAsync(
                ProvisioningUrl(setup.Project.ProjectId),
                ProvisionPayload(setup, operationId));
            injector.Callback = null;
            injector.FailurePoint = null;
            await AssertRejectedAsync(
                fenced,
                HttpStatusCode.Conflict,
                WorkbenchLeaseFenceException.ErrorCode);
            Assert.AreEqual(1, revokedAtFailureBoundary);

            Guid attemptId;
            await using (var pendingConnection = new SqlConnection(ConnectionString))
            {
                var pending = await ReadStateAsync(pendingConnection, setup.Project.ProjectId);
                Assert.IsNotNull(pending.LastAttemptId);
                attemptId = pending.LastAttemptId.Value;
                Assert.AreEqual(1, pending.Attempts);
                Assert.AreEqual(1, pending.ProvisioningAttempts);
                Assert.AreEqual(0, pending.FailedAttempts);
                Assert.AreEqual(1, pending.PendingOperations);
                Assert.AreEqual(0, pending.FailedOperations);
                Assert.AreEqual(RepositoryBindingStates.Provisioning, pending.BindingState);
                Assert.AreEqual(1, pending.AttemptedAttributions);
                Assert.AreEqual(0, pending.FailedAttributions);
                Assert.AreEqual(0, await pendingConnection.QuerySingleAsync<int>(
                    """
                    SELECT COUNT(1) FROM dbo.WorkbenchOutboxEvents
                    WHERE TenantId=1 AND ProjectId=@ProjectId
                      AND EventKind=N'RepositoryProvisioningFailed';
                    """,
                    new { setup.Project.ProjectId }));
                Assert.IsNull(pending.LastFailureCode);
                Assert.IsNull(pending.LastFailureEvidenceJson);
                Assert.IsNull(pending.LocalPath);
            }
            Assert.IsFalse(Directory.Exists(setup.Plan.TargetPath));

            var currentFence = await RefreshFenceAsync(client, setup.Project.ProjectId);
            Assert.IsTrue(currentFence.LeaseEpoch > setup.Project.LeaseEpoch);
            var recovered = await ProvisionAsync(
                client,
                setup,
                operationId,
                workbenchSessionId: currentFence.WorkbenchSessionId,
                leaseEpoch: currentFence.LeaseEpoch);
            Assert.AreEqual(attemptId, recovered.AttemptId);
            await AssertQualifiedRepositoryAsync(setup, recovered);

            await using var finalConnection = new SqlConnection(ConnectionString);
            var final = await ReadStateAsync(finalConnection, setup.Project.ProjectId);
            Assert.AreEqual(1, final.Attempts);
            Assert.AreEqual(1, final.QualifiedAttempts);
            Assert.AreEqual(0, final.FailedAttempts);
            Assert.AreEqual(1, final.CompletedOperations);
            Assert.AreEqual(0, final.FailedOperations);
            Assert.AreEqual(1, final.Receipts);
            Assert.AreEqual(1, final.CompletedAttributions);
            Assert.AreEqual(0, final.FailedAttributions);
        }
        finally
        {
            injector.Callback = null;
            injector.FailurePoint = null;
            DeleteApprovedRoot(root);
        }
    }

    [TestMethod]
    public async Task Provision_PostPublishCrashRecoversExactAttemptCommit_AndEnforcesIdempotency()
    {
        var root = CreateApprovedRoot();
        var injector = new TestProvisioningFailureInjector();
        try
        {
            using var factory = ProvisioningFactory(root, injector);
            using var client = await AuthenticatedClientAsync(factory);

            foreach (var point in new[]
                     {
                         RepositoryProvisioningFailurePoint.AfterPublish,
                         RepositoryProvisioningFailurePoint.BeforeFinalize
                     })
            {
                var setup = await ConfirmSetupAsync(client, $"Crash recovery {point}");
                var operationId = Guid.NewGuid();
                injector.FailurePoint = point;
                var firstResponse = await client.PostAsJsonAsync(
                    ProvisioningUrl(setup.Project.ProjectId),
                    ProvisionPayload(setup, operationId));
                await AssertRejectedAsync(
                    firstResponse,
                    HttpStatusCode.Conflict,
                    RepositoryProvisioningInProgressException.ErrorCode);

                Assert.IsTrue(Directory.Exists(setup.Plan.TargetPath));
                var originalHead = await GitAsync(setup.Plan.TargetPath, "rev-parse", "HEAD");
                var markerPath = Path.Combine(
                    setup.Plan.TargetPath,
                    ".git",
                    ".irondev-provisioning-attempt.json");
                Assert.IsTrue(File.Exists(markerPath));
                var originalMarker = await File.ReadAllTextAsync(markerPath);
                var publicationEvidencePath = Path.Combine(
                    setup.Plan.TargetPath,
                    ".git",
                    ".irondev-publication-evidence.json");
                Assert.IsTrue(File.Exists(publicationEvidencePath));
                var originalPublicationEvidence = await File.ReadAllTextAsync(publicationEvidencePath);
                using var publicationDocument = JsonDocument.Parse(originalPublicationEvidence);
                var publicationProperties = publicationDocument.RootElement.EnumerateObject().ToArray();
                CollectionAssert.AreEqual(
                    new[]
                    {
                        "schemaVersion", "attemptId", "planHash", "targetPathSha256", "publishedAtUtc"
                    },
                    publicationProperties.Select(property => property.Name).ToArray());
                var durablePublishedAtUtc = publicationDocument.RootElement
                    .GetProperty("publishedAtUtc")
                    .GetDateTime();
                Assert.AreEqual(DateTimeKind.Utc, durablePublishedAtUtc.Kind);

                ProvisioningState pending;
                await using (var connection = new SqlConnection(ConnectionString))
                {
                    pending = await ReadStateAsync(connection, setup.Project.ProjectId);
                    Assert.AreEqual(1, pending.Attempts);
                    Assert.AreEqual(1, pending.ProvisioningAttempts);
                    Assert.AreEqual(0, pending.Receipts);
                    Assert.AreEqual(1, pending.PendingOperations);
                    Assert.AreEqual(RepositoryBindingStates.Provisioning, pending.BindingState);
                    Assert.IsNull(pending.LocalPath);
                }

                injector.FailurePoint = null;
                if (point == RepositoryProvisioningFailurePoint.BeforeFinalize)
                {
                    var ignoredExtra = Path.Combine(setup.Plan.TargetPath, "bin", "foreign.txt");
                    Directory.CreateDirectory(Path.GetDirectoryName(ignoredExtra)!);
                    await File.WriteAllTextAsync(
                        ignoredExtra,
                        "ignored corruption must prevent qualification\n",
                        new UTF8Encoding(false));
                    var corrupted = await client.PostAsJsonAsync(
                        ProvisioningUrl(setup.Project.ProjectId),
                        ProvisionPayload(setup, operationId));
                    Assert.AreEqual(HttpStatusCode.UnprocessableEntity, corrupted.StatusCode);
                    await AssertProvisioningErrorAsync(
                        corrupted,
                        RepositoryProvisioningExecutionException.ErrorCode,
                        RepositoryProvisioningFailureCodes.PublishedRepositoryInvalid);
                    Assert.AreEqual(originalHead,
                        await GitAsync(setup.Plan.TargetPath, "rev-parse", "HEAD"));
                    Assert.IsTrue(File.Exists(ignoredExtra),
                        "Recovery verification must not clean unknown target content.");
                    Assert.AreEqual(originalMarker, await File.ReadAllTextAsync(markerPath));
                    await using var corruptedConnection = new SqlConnection(ConnectionString);
                    var rejected = await ReadStateAsync(
                        corruptedConnection, setup.Project.ProjectId);
                    Assert.AreEqual(0, rejected.Receipts);
                    Assert.AreNotEqual(RepositoryBindingStates.Qualified, rejected.BindingState);
                    continue;
                }

                Assert.AreEqual(originalMarker, await File.ReadAllTextAsync(markerPath));
                Assert.AreEqual(
                    originalPublicationEvidence,
                    await File.ReadAllTextAsync(publicationEvidencePath));
                await Task.Delay(TimeSpan.FromMilliseconds(1100));
                var recovered = await ProvisionAsync(client, setup, operationId);
                Assert.AreEqual(pending.LastAttemptId, recovered.AttemptId);
                Assert.AreEqual(originalHead, recovered.BaselineCommit);
                Assert.AreEqual(originalHead,
                    await GitAsync(setup.Plan.TargetPath, "rev-parse", "HEAD"));

                var replayResponse = await client.PostAsJsonAsync(
                    ProvisioningUrl(setup.Project.ProjectId),
                    ProvisionPayload(setup, operationId));
                Assert.AreEqual(HttpStatusCode.OK, replayResponse.StatusCode);
                var replay = await replayResponse.Content
                    .ReadFromJsonAsync<RepositoryProvisioningResult>();
                Assert.IsNotNull(replay);
                Assert.IsTrue(replay.IsReplay);
                Assert.AreEqual(recovered.AttemptId, replay.AttemptId);
                Assert.AreEqual(recovered.ReceiptId, replay.ReceiptId);
                Assert.AreEqual(recovered.BaselineCommit, replay.BaselineCommit);

                await using (var receiptConnection = new SqlConnection(ConnectionString))
                {
                    var receipt = await receiptConnection.QuerySingleAsync<ProvisioningReceiptTimes>(
                        """
                        SELECT PublishedAtUtc, RecordedAtUtc, ReceiptJson
                        FROM dbo.RepositoryProvisioningReceipts
                        WHERE TenantId=1 AND ProjectId=@ProjectId
                          AND ProvisioningAttemptId=@AttemptId;
                        """,
                        new { setup.Project.ProjectId, AttemptId = recovered.AttemptId });
                    Assert.AreEqual(durablePublishedAtUtc.Ticks, receipt.PublishedAtUtc.Ticks,
                        "Recovery must preserve the pre-move publication timestamp exactly.");
                    Assert.IsTrue(
                        receipt.RecordedAtUtc >= durablePublishedAtUtc.AddMilliseconds(750),
                        "RecordedAtUtc must describe later SQL finalization, not rewrite publication time.");
                    using var receiptDocument = JsonDocument.Parse(receipt.ReceiptJson);
                    Assert.AreEqual(
                        durablePublishedAtUtc.Ticks,
                        receiptDocument.RootElement.GetProperty("publishedAtUtc").GetDateTime().Ticks);
                }

                var mismatch = await client.PostAsJsonAsync(
                    ProvisioningUrl(setup.Project.ProjectId),
                    ProvisionPayload(
                        setup,
                        operationId,
                        setup.Confirmation.RepositoryBinding.Revision,
                        setup.Confirmation.ExecutionProfile.Revision + 1));
                Assert.AreEqual(HttpStatusCode.Conflict, mismatch.StatusCode);
                await AssertErrorAsync(mismatch, ProjectStartOperationMismatchException.ErrorCode);

                var distinct = await client.PostAsJsonAsync(
                    ProvisioningUrl(setup.Project.ProjectId),
                    ProvisionPayload(setup, Guid.NewGuid()));
                Assert.AreEqual(HttpStatusCode.Conflict, distinct.StatusCode);
                await AssertErrorAsync(distinct, RepositoryProvisioningStaleException.ErrorCode);

                await using var finalConnection = new SqlConnection(ConnectionString);
                var final = await ReadStateAsync(finalConnection, setup.Project.ProjectId);
                Assert.AreEqual(1, final.Attempts);
                Assert.AreEqual(1, final.QualifiedAttempts);
                Assert.AreEqual(1, final.Receipts);
                Assert.AreEqual(1, final.CompletedOperations);
                Assert.AreEqual(1, final.AttemptedAttributions);
                Assert.AreEqual(1, final.CompletedAttributions);
            }
        }
        finally
        {
            injector.FailurePoint = null;
            DeleteApprovedRoot(root);
        }
    }

    [TestMethod]
    public async Task Provision_RejectsEmptyChildCommitEvenWhenTreeAndCopiedTrailersMatch()
    {
        var root = CreateApprovedRoot();
        var injector = new TestProvisioningFailureInjector
        {
            FailurePoint = RepositoryProvisioningFailurePoint.AfterPublish
        };
        try
        {
            using var factory = ProvisioningFactory(root, injector);
            using var client = await AuthenticatedClientAsync(factory);
            var setup = await ConfirmSetupAsync(client, "Copied trailer commit rejection");
            var operationId = Guid.NewGuid();
            var interrupted = await client.PostAsJsonAsync(
                ProvisioningUrl(setup.Project.ProjectId),
                ProvisionPayload(setup, operationId));
            await AssertRejectedAsync(
                interrupted,
                HttpStatusCode.Conflict,
                RepositoryProvisioningInProgressException.ErrorCode);
            injector.FailurePoint = null;

            var originalHead = await GitAsync(setup.Plan.TargetPath, "rev-parse", "HEAD");
            var originalTree = await GitAsync(setup.Plan.TargetPath, "rev-parse", "HEAD^{tree}");
            var copiedMessage = await GitAsync(setup.Plan.TargetPath, "log", "-1", "--format=%B");
            _ = await GitAsync(
                setup.Plan.TargetPath,
                "-c", "user.name=Adversarial Test",
                "-c", "user.email=adversarial@irondev.invalid",
                "-c", "commit.gpgSign=false",
                "commit", "--allow-empty", "--no-gpg-sign", "--no-verify",
                "--message", copiedMessage);
            var childHead = await GitAsync(setup.Plan.TargetPath, "rev-parse", "HEAD");
            Assert.AreNotEqual(originalHead, childHead);
            Assert.AreEqual(originalTree,
                await GitAsync(setup.Plan.TargetPath, "rev-parse", "HEAD^{tree}"));

            var rejected = await client.PostAsJsonAsync(
                ProvisioningUrl(setup.Project.ProjectId),
                ProvisionPayload(setup, operationId));
            Assert.AreEqual(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
            await AssertProvisioningErrorAsync(
                rejected,
                RepositoryProvisioningExecutionException.ErrorCode,
                RepositoryProvisioningFailureCodes.PublishedRepositoryInvalid);
            Assert.AreEqual(childHead,
                await GitAsync(setup.Plan.TargetPath, "rev-parse", "HEAD"));

            await using var connection = new SqlConnection(ConnectionString);
            var state = await ReadStateAsync(connection, setup.Project.ProjectId);
            Assert.AreEqual(1, state.Attempts);
            Assert.AreEqual(1, state.FailedAttempts);
            Assert.AreEqual(0, state.Receipts);
            Assert.AreEqual(RepositoryBindingStates.ProvisioningFailed, state.BindingState);
            Assert.AreEqual(RepositoryProvisioningFailureCodes.PublishedRepositoryInvalid,
                state.LastFailureCode);
            Assert.AreEqual(1, state.AttemptedAttributions);
            Assert.AreEqual(1, state.FailedAttributions);
        }
        finally
        {
            injector.FailurePoint = null;
            DeleteApprovedRoot(root);
        }
    }

    [TestMethod]
    public async Task Provision_RejectsRootCommitWithSubstitutedBlobAndHiddenIndexFlags()
    {
        var root = CreateApprovedRoot();
        var injector = new TestProvisioningFailureInjector
        {
            FailurePoint = RepositoryProvisioningFailurePoint.AfterPublish
        };
        try
        {
            using var factory = ProvisioningFactory(root, injector);
            using var client = await AuthenticatedClientAsync(factory);
            var setup = await ConfirmSetupAsync(client, "Git tree binding attack rejection");
            var operationId = Guid.NewGuid();
            var interrupted = await client.PostAsJsonAsync(
                ProvisioningUrl(setup.Project.ProjectId),
                ProvisionPayload(setup, operationId));
            await AssertRejectedAsync(
                interrupted,
                HttpStatusCode.Conflict,
                RepositoryProvisioningInProgressException.ErrorCode);
            injector.FailurePoint = null;

            const string attackedPath = "global.json";
            var fullPath = Path.Combine(setup.Plan.TargetPath, attackedPath);
            var expectedBytes = await File.ReadAllBytesAsync(fullPath);
            var copiedMessage = await GitAsync(setup.Plan.TargetPath, "log", "-1", "--format=%B");
            await File.WriteAllTextAsync(
                fullPath,
                "{\"sdk\":{\"version\":\"0.0.0-attacker\"}}\n",
                new UTF8Encoding(false));
            _ = await GitAsync(setup.Plan.TargetPath, "add", "--", attackedPath);
            var substitutedTree = await GitAsync(setup.Plan.TargetPath, "write-tree");
            var substitutedRootCommit = await GitAsync(
                setup.Plan.TargetPath,
                "-c", "user.name=Adversarial Test",
                "-c", "user.email=adversarial@irondev.invalid",
                "-c", "commit.gpgSign=false",
                "commit-tree", substitutedTree,
                "-m", copiedMessage);
            _ = await GitAsync(
                setup.Plan.TargetPath,
                "update-ref", "refs/heads/main", substitutedRootCommit);
            await File.WriteAllBytesAsync(fullPath, expectedBytes);
            _ = await GitAsync(
                setup.Plan.TargetPath,
                "update-index", "--skip-worktree", "--", attackedPath);
            Assert.AreEqual(string.Empty,
                await GitAsync(setup.Plan.TargetPath, "status", "--porcelain=v1", "--untracked-files=all"));
            var parentLine = await GitAsync(
                setup.Plan.TargetPath, "rev-list", "--parents", "-n", "1", "HEAD");
            Assert.AreEqual(1, parentLine.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length,
                "The substituted commit is deliberately still a raw root commit.");

            var rejected = await client.PostAsJsonAsync(
                ProvisioningUrl(setup.Project.ProjectId),
                ProvisionPayload(setup, operationId));
            Assert.AreEqual(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
            await AssertProvisioningErrorAsync(
                rejected,
                RepositoryProvisioningExecutionException.ErrorCode,
                RepositoryProvisioningFailureCodes.PublishedRepositoryInvalid);
            Assert.AreEqual(substitutedRootCommit,
                await GitAsync(setup.Plan.TargetPath, "rev-parse", "HEAD"));

            await using var connection = new SqlConnection(ConnectionString);
            var state = await ReadStateAsync(connection, setup.Project.ProjectId);
            Assert.AreEqual(1, state.FailedAttempts);
            Assert.AreEqual(0, state.Receipts);
            Assert.AreEqual(RepositoryBindingStates.ProvisioningFailed, state.BindingState);
        }
        finally
        {
            injector.FailurePoint = null;
            DeleteApprovedRoot(root);
        }
    }

    [TestMethod]
    public async Task Provision_RejectsGitObjectAlternatesControlPlaneSubstitution()
    {
        var root = CreateApprovedRoot();
        var injector = new TestProvisioningFailureInjector
        {
            FailurePoint = RepositoryProvisioningFailurePoint.AfterPublish
        };
        try
        {
            using var factory = ProvisioningFactory(root, injector);
            using var client = await AuthenticatedClientAsync(factory);
            var setup = await ConfirmSetupAsync(client, "Git alternates rejection");
            var operationId = Guid.NewGuid();
            var interrupted = await client.PostAsJsonAsync(
                ProvisioningUrl(setup.Project.ProjectId),
                ProvisionPayload(setup, operationId));
            await AssertRejectedAsync(
                interrupted,
                HttpStatusCode.Conflict,
                RepositoryProvisioningInProgressException.ErrorCode);
            injector.FailurePoint = null;

            var foreignObjects = Path.Combine(root, "foreign-object-database");
            Directory.CreateDirectory(foreignObjects);
            var alternates = Path.Combine(
                setup.Plan.TargetPath, ".git", "objects", "info", "alternates");
            await File.WriteAllTextAsync(
                alternates,
                foreignObjects + "\n",
                new UTF8Encoding(false));

            var rejected = await client.PostAsJsonAsync(
                ProvisioningUrl(setup.Project.ProjectId),
                ProvisionPayload(setup, operationId));
            Assert.AreEqual(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
            await AssertProvisioningErrorAsync(
                rejected,
                RepositoryProvisioningExecutionException.ErrorCode,
                RepositoryProvisioningFailureCodes.PublishedRepositoryInvalid);
            Assert.IsTrue(File.Exists(alternates),
                "Recovery rejection must preserve suspicious control-plane evidence.");

            await using var connection = new SqlConnection(ConnectionString);
            var state = await ReadStateAsync(connection, setup.Project.ProjectId);
            Assert.AreEqual(1, state.FailedAttempts);
            Assert.AreEqual(0, state.Receipts);
            Assert.AreEqual(RepositoryBindingStates.ProvisioningFailed, state.BindingState);
        }
        finally
        {
            injector.FailurePoint = null;
            DeleteApprovedRoot(root);
        }
    }

    [TestMethod]
    public async Task Provision_NeverAdoptsUnknownTargetOrMismatchedStaging_ButRestartsExactOwnedStaging()
    {
        var root = CreateApprovedRoot();
        var injector = new TestProvisioningFailureInjector();
        try
        {
            using var factory = ProvisioningFactory(root, injector);
            using var client = await AuthenticatedClientAsync(factory);

            var unknownTargetSetup = await ConfirmSetupAsync(client, "Unknown target preservation");
            Directory.CreateDirectory(Path.Combine(unknownTargetSetup.Plan.TargetPath, ".git"));
            var unknownMarker = Path.Combine(
                unknownTargetSetup.Plan.TargetPath,
                ".git",
                ".irondev-provisioning-attempt.json");
            await File.WriteAllTextAsync(unknownMarker, "{\"attemptId\":\"not-irondev-owned\"}\n");
            var unknownKeep = Path.Combine(unknownTargetSetup.Plan.TargetPath, "keep.txt");
            await File.WriteAllTextAsync(unknownKeep, "unknown target must survive\n");
            var unknownResponse = await client.PostAsJsonAsync(
                ProvisioningUrl(unknownTargetSetup.Project.ProjectId),
                ProvisionPayload(unknownTargetSetup, Guid.NewGuid()));
            Assert.AreEqual(HttpStatusCode.UnprocessableEntity, unknownResponse.StatusCode);
            await AssertProvisioningErrorAsync(
                unknownResponse,
                RepositoryProvisioningExecutionException.ErrorCode,
                RepositoryProvisioningFailureCodes.TargetAlreadyExists);
            Assert.IsTrue(File.Exists(unknownKeep));
            Assert.AreEqual("unknown target must survive\n", await File.ReadAllTextAsync(unknownKeep));

            var mismatchedSetup = await ConfirmSetupAsync(client, "Mismatched staging preservation");
            string? mismatchedStaging = null;
            injector.Callback = point =>
            {
                if (point != RepositoryProvisioningFailurePoint.ClaimCommitted)
                    return;
                mismatchedStaging = SeedAttemptStaging(
                    mismatchedSetup.Project.ProjectId,
                    mismatchedSetup.Plan,
                    exactMarker: false);
            };
            var mismatchedResponse = await client.PostAsJsonAsync(
                ProvisioningUrl(mismatchedSetup.Project.ProjectId),
                ProvisionPayload(mismatchedSetup, Guid.NewGuid()));
            injector.Callback = null;
            Assert.AreEqual(HttpStatusCode.UnprocessableEntity, mismatchedResponse.StatusCode);
            await AssertProvisioningErrorAsync(
                mismatchedResponse,
                RepositoryProvisioningExecutionException.ErrorCode,
                RepositoryProvisioningFailureCodes.FileSystemFailed);
            Assert.IsNotNull(mismatchedStaging);
            Assert.IsTrue(File.Exists(Path.Combine(mismatchedStaging, "foreign-owner.txt")),
                "A mismatched pre-existing staging directory must never be deleted.");

            var toctouSetup = await ConfirmSetupAsync(client, "Atomic staging TOCTOU preservation");
            string? racedStaging = null;
            injector.Callback = point =>
            {
                if (point != RepositoryProvisioningFailurePoint.BeforeStagingCreate)
                    return;
                racedStaging = SeedAttemptStaging(
                    toctouSetup.Project.ProjectId,
                    toctouSetup.Plan,
                    exactMarker: false);
            };
            var racedResponse = await client.PostAsJsonAsync(
                ProvisioningUrl(toctouSetup.Project.ProjectId),
                ProvisionPayload(toctouSetup, Guid.NewGuid()));
            injector.Callback = null;
            Assert.AreEqual(HttpStatusCode.UnprocessableEntity, racedResponse.StatusCode);
            await AssertProvisioningErrorAsync(
                racedResponse,
                RepositoryProvisioningExecutionException.ErrorCode,
                RepositoryProvisioningFailureCodes.FileSystemFailed);
            Assert.IsNotNull(racedStaging);
            Assert.IsTrue(File.Exists(Path.Combine(racedStaging, "foreign-owner.txt")),
                "An unknown directory won exactly at atomic create must survive unchanged.");
            Assert.IsFalse(Directory.Exists(toctouSetup.Plan.TargetPath));
            await using (var racedConnection = new SqlConnection(ConnectionString))
            {
                var raced = await ReadStateAsync(
                    racedConnection, toctouSetup.Project.ProjectId);
                Assert.AreEqual(1, raced.FailedAttempts);
                Assert.AreEqual(0, raced.Receipts);
                Assert.IsNull(raced.LocalPath);
            }

            var exactSetup = await ConfirmSetupAsync(client, "Exact staging recovery");
            string? exactStaging = null;
            injector.Callback = point =>
            {
                if (point != RepositoryProvisioningFailurePoint.ClaimCommitted)
                    return;
                exactStaging = SeedAttemptStaging(
                    exactSetup.Project.ProjectId,
                    exactSetup.Plan,
                    exactMarker: true);
            };
            var exactResult = await ProvisionAsync(client, exactSetup, Guid.NewGuid());
            injector.Callback = null;
            Assert.IsNotNull(exactStaging);
            Assert.IsFalse(Directory.Exists(exactStaging),
                "Exact marker-owned staging must be removed before a deterministic restart.");
            await AssertQualifiedRepositoryAsync(exactSetup, exactResult);
        }
        finally
        {
            injector.Callback = null;
            DeleteApprovedRoot(root);
        }
    }

    [TestMethod]
    public async Task Provision_ClaimRequiresTenantMembershipContributorRoleAndExactCurrentFence()
    {
        var root = CreateApprovedRoot();
        try
        {
            using var factory = ProvisioningFactory(root);
            using var owner = await AuthenticatedClientAsync(factory);

            var fenced = await ConfirmSetupAsync(owner, "Claim fence boundary");
            var wrongSession = await owner.PostAsJsonAsync(
                ProvisioningUrl(fenced.Project.ProjectId),
                new
                {
                    workbenchSessionId = fenced.Project.WorkbenchSessionId + 100_000,
                    fenced.Project.LeaseEpoch,
                    clientOperationId = Guid.NewGuid(),
                    setupConfirmationId = fenced.Confirmation.ConfirmationId,
                    expectedRepositoryBindingRevision = fenced.Confirmation.RepositoryBinding.Revision,
                    expectedExecutionProfileRevision = fenced.Confirmation.ExecutionProfile.Revision
                });
            await AssertRejectedAsync(
                wrongSession,
                HttpStatusCode.Conflict,
                WorkbenchLeaseFenceException.ErrorCode);

            var wrongEpoch = await owner.PostAsJsonAsync(
                ProvisioningUrl(fenced.Project.ProjectId),
                new
                {
                    fenced.Project.WorkbenchSessionId,
                    leaseEpoch = fenced.Project.LeaseEpoch + 1,
                    clientOperationId = Guid.NewGuid(),
                    setupConfirmationId = fenced.Confirmation.ConfirmationId,
                    expectedRepositoryBindingRevision = fenced.Confirmation.RepositoryBinding.Revision,
                    expectedExecutionProfileRevision = fenced.Confirmation.ExecutionProfile.Revision
                });
            await AssertRejectedAsync(
                wrongEpoch,
                HttpStatusCode.Conflict,
                WorkbenchLeaseFenceException.ErrorCode);

            foreach (var stalePayload in new[]
                     {
                         ProvisionPayload(fenced, Guid.NewGuid(),
                             fenced.Confirmation.RepositoryBinding.Revision + 1,
                             fenced.Confirmation.ExecutionProfile.Revision),
                         ProvisionPayload(fenced, Guid.NewGuid(),
                             fenced.Confirmation.RepositoryBinding.Revision,
                             fenced.Confirmation.ExecutionProfile.Revision + 1),
                         ProvisionPayload(fenced, Guid.NewGuid(),
                             setupConfirmationId: Guid.NewGuid())
                     })
            {
                var stale = await owner.PostAsJsonAsync(
                    ProvisioningUrl(fenced.Project.ProjectId), stalePayload);
                await AssertRejectedAsync(
                    stale,
                    HttpStatusCode.Conflict,
                    RepositoryProvisioningStaleException.ErrorCode);
            }

            var viewerSetup = await ConfirmSetupAsync(owner, "Viewer provisioning boundary");
            using (var viewer = await CreateProjectUserClientAsync(
                       owner, factory, viewerSetup.Project.ProjectId, "Viewer", "Viewer"))
            {
                var viewerResponse = await viewer.PostAsJsonAsync(
                    ProvisioningUrl(viewerSetup.Project.ProjectId),
                    ProvisionPayload(viewerSetup, Guid.NewGuid()));
                await AssertRejectedAsync(
                    viewerResponse,
                    HttpStatusCode.Forbidden,
                    RepositoryProvisioningForbiddenException.ErrorCode);
            }

            var holderSetup = await ConfirmSetupAsync(owner, "Holder provisioning boundary");
            using (var contributor = await CreateProjectUserClientAsync(
                       owner, factory, holderSetup.Project.ProjectId, "Member", "Contributor"))
            {
                var wrongHolder = await contributor.PostAsJsonAsync(
                    ProvisioningUrl(holderSetup.Project.ProjectId),
                    ProvisionPayload(holderSetup, Guid.NewGuid()));
                await AssertRejectedAsync(
                    wrongHolder,
                    HttpStatusCode.Conflict,
                    WorkbenchLeaseFenceException.ErrorCode);
            }

            var nonmemberSetup = await ConfirmSetupAsync(owner, "Nonmember provisioning boundary");
            using (var nonmember = await CreateProjectUserClientAsync(
                       owner, factory, projectId: null, "Viewer", "Viewer"))
            {
                var concealed = await nonmember.PostAsJsonAsync(
                    ProvisioningUrl(nonmemberSetup.Project.ProjectId),
                    ProvisionPayload(nonmemberSetup, Guid.NewGuid()));
                await AssertRejectedAsync(concealed, HttpStatusCode.NotFound, "project_not_found");
            }

            var expiredSetup = await ConfirmSetupAsync(owner, "Expired provisioning boundary");
            var revokedSetup = await ConfirmSetupAsync(owner, "Revoked epoch provisioning boundary");
            await using (var connection = new SqlConnection(ConnectionString))
            {
                await connection.ExecuteAsync(
                    """
                    UPDATE dbo.WorkbenchWriteLeases
                    SET ExpiresAtUtc=DATEADD(MINUTE, -1, SYSUTCDATETIME())
                    WHERE TenantId=1 AND ProjectId=@ExpiredProjectId
                      AND WorkbenchSessionId=@ExpiredSessionId AND LeaseEpoch=@ExpiredEpoch;
                    UPDATE dbo.WorkbenchWriteLeases
                    SET RevokedAtUtc=SYSUTCDATETIME()
                    WHERE TenantId=1 AND ProjectId=@RevokedProjectId
                      AND WorkbenchSessionId=@RevokedSessionId AND LeaseEpoch=@RevokedEpoch;
                    """,
                    new
                    {
                        ExpiredProjectId = expiredSetup.Project.ProjectId,
                        ExpiredSessionId = expiredSetup.Project.WorkbenchSessionId,
                        ExpiredEpoch = expiredSetup.Project.LeaseEpoch,
                        RevokedProjectId = revokedSetup.Project.ProjectId,
                        RevokedSessionId = revokedSetup.Project.WorkbenchSessionId,
                        RevokedEpoch = revokedSetup.Project.LeaseEpoch
                    });
            }
            foreach (var invalidLease in new[] { expiredSetup, revokedSetup })
            {
                var response = await owner.PostAsJsonAsync(
                    ProvisioningUrl(invalidLease.Project.ProjectId),
                    ProvisionPayload(invalidLease, Guid.NewGuid()));
                await AssertRejectedAsync(
                    response,
                    HttpStatusCode.Conflict,
                    WorkbenchLeaseFenceException.ErrorCode);
            }

            var crossTenantSetup = await ConfirmSetupAsync(owner, "Cross tenant provisioning boundary");
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
                var response = await crossTenant.PostAsJsonAsync(
                    ProvisioningUrl(crossTenantSetup.Project.ProjectId),
                    ProvisionPayload(crossTenantSetup, Guid.NewGuid()));
                await AssertRejectedAsync(response, HttpStatusCode.NotFound, "project_not_found");
            }

            foreach (var setup in new[]
                     {
                         fenced, viewerSetup, holderSetup, nonmemberSetup,
                         expiredSetup, revokedSetup, crossTenantSetup
                     })
            {
                await using var connection = new SqlConnection(ConnectionString);
                var state = await ReadStateAsync(connection, setup.Project.ProjectId);
                Assert.AreEqual(0, state.Attempts,
                    "Rejected claim authority must not create a provisioning attempt.");
                Assert.AreEqual(RepositoryBindingStates.SetupConfirmed, state.BindingState);
                Assert.IsNull(state.LocalPath);
                Assert.IsFalse(Directory.Exists(setup.Plan.TargetPath));
            }
        }
        finally
        {
            DeleteApprovedRoot(root);
        }
    }

    [TestMethod]
    public async Task Provision_FinalizationRechecksEveryMembershipRoleSessionHolderAndLeaseFence()
    {
        var root = CreateApprovedRoot();
        var injector = new TestProvisioningFailureInjector();
        try
        {
            using var factory = ProvisioningFactory(root, injector);
            using var client = await AuthenticatedClientAsync(factory);

            foreach (var mutation in Enum.GetValues<FinalizeFenceMutation>())
            {
                var setup = await ConfirmSetupAsync(client, $"Finalize fence {mutation}");
                var operationId = Guid.NewGuid();
                injector.Callback = point =>
                {
                    if (point == RepositoryProvisioningFailurePoint.BeforeFinalize)
                        ApplyFinalizeFenceMutation(setup.Project, mutation);
                };

                var blocked = await client.PostAsJsonAsync(
                    ProvisioningUrl(setup.Project.ProjectId),
                    ProvisionPayload(setup, operationId));
                injector.Callback = null;
                Assert.IsFalse(blocked.IsSuccessStatusCode,
                    $"Finalize unexpectedly succeeded after {mutation}.");
                Assert.AreEqual(ExpectedFinalizeFenceStatus(mutation), blocked.StatusCode,
                    await blocked.Content.ReadAsStringAsync());

                var publishedHead = await GitAsync(setup.Plan.TargetPath, "rev-parse", "HEAD");
                await using (var connection = new SqlConnection(ConnectionString))
                {
                    var pending = await ReadStateAsync(connection, setup.Project.ProjectId);
                    Assert.AreEqual(1, pending.ProvisioningAttempts);
                    Assert.AreEqual(0, pending.Receipts);
                    Assert.AreEqual(1, pending.PendingOperations);
                    Assert.AreEqual(RepositoryBindingStates.Provisioning, pending.BindingState);
                    Assert.IsNull(pending.LocalPath);
                }

                if (mutation is FinalizeFenceMutation.TenantMembership or
                    FinalizeFenceMutation.ProjectMembership or
                    FinalizeFenceMutation.ProjectRole)
                    RestoreFinalizeFence(setup.Project, mutation);

                var recoveryFence = setup.Project;
                if (mutation is FinalizeFenceMutation.Session or
                    FinalizeFenceMutation.Holder or
                    FinalizeFenceMutation.RevokedEpoch or
                    FinalizeFenceMutation.Expiry)
                    recoveryFence = await RefreshFenceAsync(client, setup.Project.ProjectId);

                RepositoryProvisioningResult recovered;
                try
                {
                    recovered = await ProvisionAsync(
                        client,
                        setup,
                        operationId,
                        workbenchSessionId: recoveryFence.WorkbenchSessionId,
                        leaseEpoch: recoveryFence.LeaseEpoch);
                }
                catch (AssertFailedException exception)
                {
                    throw new AssertFailedException(
                        $"Exact published recovery failed after finalize mutation {mutation}: " +
                        exception.Message,
                        exception);
                }
                Assert.AreEqual(publishedHead, recovered.BaselineCommit);
                Assert.AreEqual(publishedHead,
                    await GitAsync(setup.Plan.TargetPath, "rev-parse", "HEAD"));
                await AssertQualifiedRepositoryAsync(setup, recovered);

                if (recoveryFence != setup.Project)
                {
                    var changedImmutableRevision = await client.PostAsJsonAsync(
                        ProvisioningUrl(setup.Project.ProjectId),
                        ProvisionPayload(
                            setup,
                            operationId,
                            bindingRevision: setup.Confirmation.RepositoryBinding.Revision + 1,
                            workbenchSessionId: recoveryFence.WorkbenchSessionId,
                            leaseEpoch: recoveryFence.LeaseEpoch));
                    await AssertRejectedAsync(
                        changedImmutableRevision,
                        HttpStatusCode.Conflict,
                        ProjectStartOperationMismatchException.ErrorCode);
                }
            }
        }
        finally
        {
            injector.Callback = null;
            DeleteApprovedRoot(root);
        }
    }

    [TestMethod]
    public async Task Provision_RecoveredPublishedAttemptRechecksFenceAfterVerificationBeforeFinalization()
    {
        var root = CreateApprovedRoot();
        var injector = new TestProvisioningFailureInjector
        {
            FailurePoint = RepositoryProvisioningFailurePoint.AfterPublish
        };
        try
        {
            using var factory = ProvisioningFactory(root, injector);
            using var client = await AuthenticatedClientAsync(factory);
            var setup = await ConfirmSetupAsync(client, "Recovered finalize fence race");
            var operationId = Guid.NewGuid();

            var interrupted = await client.PostAsJsonAsync(
                ProvisioningUrl(setup.Project.ProjectId),
                ProvisionPayload(setup, operationId));
            await AssertRejectedAsync(
                interrupted,
                HttpStatusCode.Conflict,
                RepositoryProvisioningInProgressException.ErrorCode);
            injector.FailurePoint = null;

            var publishedHead = await GitAsync(setup.Plan.TargetPath, "rev-parse", "HEAD");
            Guid attemptId;
            await using (var pendingConnection = new SqlConnection(ConnectionString))
            {
                var pending = await ReadStateAsync(pendingConnection, setup.Project.ProjectId);
                Assert.IsNotNull(pending.LastAttemptId);
                attemptId = pending.LastAttemptId.Value;
                Assert.AreEqual(1, pending.ProvisioningAttempts);
                Assert.AreEqual(0, pending.Receipts);
                Assert.IsNull(pending.LocalPath);
            }

            var fenceRevokedAtRecoveredFinalizeBoundary = 0;
            injector.Callback = point =>
            {
                if (point != RepositoryProvisioningFailurePoint.BeforeFinalize ||
                    Interlocked.Exchange(ref fenceRevokedAtRecoveredFinalizeBoundary, 1) != 0)
                    return;
                ApplyFinalizeFenceMutation(setup.Project, FinalizeFenceMutation.RevokedEpoch);
            };
            var fencedOut = await client.PostAsJsonAsync(
                ProvisioningUrl(setup.Project.ProjectId),
                ProvisionPayload(setup, operationId));
            injector.Callback = null;
            await AssertRejectedAsync(
                fencedOut,
                HttpStatusCode.Conflict,
                WorkbenchLeaseFenceException.ErrorCode);
            Assert.AreEqual(1, fenceRevokedAtRecoveredFinalizeBoundary,
                "The fence race must occur after recovered repository verification.");

            await using (var rejectedConnection = new SqlConnection(ConnectionString))
            {
                var rejected = await ReadStateAsync(rejectedConnection, setup.Project.ProjectId);
                Assert.AreEqual(1, rejected.ProvisioningAttempts);
                Assert.AreEqual(0, rejected.QualifiedAttempts);
                Assert.AreEqual(0, rejected.Receipts);
                Assert.AreEqual(1, rejected.PendingOperations);
                Assert.AreEqual(RepositoryBindingStates.Provisioning, rejected.BindingState);
                Assert.IsNull(rejected.LocalPath,
                    "A recovered verification must not project the path after losing its fence.");
            }
            Assert.AreEqual(publishedHead,
                await GitAsync(setup.Plan.TargetPath, "rev-parse", "HEAD"));

            var takeoverFence = await RefreshFenceAsync(client, setup.Project.ProjectId);
            Assert.IsTrue(
                takeoverFence.WorkbenchSessionId != setup.Project.WorkbenchSessionId ||
                takeoverFence.LeaseEpoch != setup.Project.LeaseEpoch,
                "Recovery must run under a newly current fence.");
            var recovered = await ProvisionAsync(
                client,
                setup,
                operationId,
                workbenchSessionId: takeoverFence.WorkbenchSessionId,
                leaseEpoch: takeoverFence.LeaseEpoch);
            Assert.AreEqual(attemptId, recovered.AttemptId);
            Assert.AreEqual(publishedHead, recovered.BaselineCommit);

            await using var finalConnection = new SqlConnection(ConnectionString);
            var final = await ReadStateAsync(finalConnection, setup.Project.ProjectId);
            Assert.AreEqual(1, final.Attempts);
            Assert.AreEqual(1, final.QualifiedAttempts);
            Assert.AreEqual(1, final.Receipts);
            Assert.AreEqual(1, final.CompletedOperations);
            Assert.AreEqual(RepositoryBindingStates.Qualified, final.BindingState);
            Assert.AreEqual(setup.Plan.TargetPath, final.LocalPath);
        }
        finally
        {
            injector.FailurePoint = null;
            injector.Callback = null;
            DeleteApprovedRoot(root);
        }
    }

    [TestMethod]
    public async Task Provision_ConcurrentExactOperationAcrossIndependentHostsPublishesOnlyOnce()
    {
        var root = CreateApprovedRoot();
        try
        {
            using var firstFactory = ProvisioningFactory(root);
            using var secondFactory = ProvisioningFactory(root);
            using var firstClient = await AuthenticatedClientAsync(firstFactory);
            using var secondClient = await AuthenticatedClientAsync(secondFactory);
            var setup = await ConfirmSetupAsync(firstClient, "Concurrent exact operation");
            var operationId = Guid.NewGuid();
            var payload = ProvisionPayload(setup, operationId);

            var requests = await Task.WhenAll(
                firstClient.PostAsJsonAsync(ProvisioningUrl(setup.Project.ProjectId), payload),
                secondClient.PostAsJsonAsync(ProvisioningUrl(setup.Project.ProjectId), payload));
            foreach (var response in requests)
            {
                Assert.IsTrue(
                    response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict,
                    $"Concurrent exact request returned {response.StatusCode}: " +
                    await response.Content.ReadAsStringAsync());
                if (response.StatusCode == HttpStatusCode.Conflict)
                    await AssertErrorAsync(response, RepositoryProvisioningInProgressException.ErrorCode);
            }
            Assert.IsTrue(requests.Any(response => response.StatusCode == HttpStatusCode.OK));

            var successful = new List<RepositoryProvisioningResult>();
            foreach (var response in requests.Where(response => response.StatusCode == HttpStatusCode.OK))
            {
                var value = await response.Content.ReadFromJsonAsync<RepositoryProvisioningResult>();
                Assert.IsNotNull(value);
                successful.Add(value);
            }
            Assert.AreEqual(1, successful.Select(value => value.AttemptId).Distinct().Count());
            Assert.AreEqual(1, successful.Select(value => value.ReceiptId).Distinct().Count());
            Assert.AreEqual(1, successful.Select(value => value.BaselineCommit).Distinct().Count());

            var reacquired = await secondClient.PostAsJsonAsync(
                ProvisioningUrl(setup.Project.ProjectId), payload);
            Assert.AreEqual(HttpStatusCode.OK, reacquired.StatusCode,
                await reacquired.Content.ReadAsStringAsync());
            var reacquiredReplay = await reacquired.Content
                .ReadFromJsonAsync<RepositoryProvisioningResult>();
            Assert.IsNotNull(reacquiredReplay);
            Assert.IsTrue(reacquiredReplay.IsReplay,
                "The SQL session app lock must be released so a later exact request can reacquire and replay.");
            Assert.AreEqual(successful[0].AttemptId, reacquiredReplay.AttemptId);

            await using var connection = new SqlConnection(ConnectionString);
            var state = await ReadStateAsync(connection, setup.Project.ProjectId);
            Assert.AreEqual(1, state.Attempts);
            Assert.AreEqual(1, state.QualifiedAttempts);
            Assert.AreEqual(1, state.Receipts);
            Assert.AreEqual(1, state.CompletedOperations);
            Assert.AreEqual(successful[0].BaselineCommit,
                await GitAsync(setup.Plan.TargetPath, "rev-parse", "HEAD"));
            Assert.IsFalse(Directory.EnumerateDirectories(root)
                .Any(path => path.EndsWith(".staging", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            DeleteApprovedRoot(root);
        }
    }

    [TestMethod]
    public async Task Provision_AppLockContentionDoesNotLeakActivityToViewerOrNonmember()
    {
        var root = CreateApprovedRoot();
        var injector = new TestProvisioningFailureInjector();
        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        try
        {
            using var factory = ProvisioningFactory(root, injector);
            using var owner = await AuthenticatedClientAsync(factory);
            var setup = await ConfirmSetupAsync(owner, "App lock authorization concealment");
            using var viewer = await CreateProjectUserClientAsync(
                owner, factory, setup.Project.ProjectId, "Viewer", "Viewer");
            using var nonmember = await CreateProjectUserClientAsync(
                owner, factory, projectId: null, "Viewer", "Viewer");
            var operationId = Guid.NewGuid();
            injector.Callback = point =>
            {
                if (point != RepositoryProvisioningFailurePoint.ClaimCommitted)
                    return;
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(30)))
                    throw new TimeoutException("Timed out holding the provisioning app lock.");
            };

            var ownerRequest = owner.PostAsJsonAsync(
                ProvisioningUrl(setup.Project.ProjectId),
                ProvisionPayload(setup, operationId));
            Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(30)),
                "The owner request did not reach the durable claimed app-lock boundary.");

            var viewerResponse = await viewer.PostAsJsonAsync(
                ProvisioningUrl(setup.Project.ProjectId),
                ProvisionPayload(setup, operationId));
            await AssertRejectedAsync(
                viewerResponse,
                HttpStatusCode.Forbidden,
                RepositoryProvisioningForbiddenException.ErrorCode);

            var nonmemberResponse = await nonmember.PostAsJsonAsync(
                ProvisioningUrl(setup.Project.ProjectId),
                ProvisionPayload(setup, operationId));
            await AssertRejectedAsync(
                nonmemberResponse,
                HttpStatusCode.NotFound,
                "project_not_found");

            release.Set();
            injector.Callback = null;
            var ownerResponse = await ownerRequest;
            Assert.AreEqual(HttpStatusCode.OK, ownerResponse.StatusCode,
                await ownerResponse.Content.ReadAsStringAsync());
            var result = await ownerResponse.Content
                .ReadFromJsonAsync<RepositoryProvisioningResult>();
            Assert.IsNotNull(result);

            await using var connection = new SqlConnection(ConnectionString);
            var state = await ReadStateAsync(connection, setup.Project.ProjectId);
            Assert.AreEqual(1, state.Attempts);
            Assert.AreEqual(1, state.Receipts);
            Assert.AreEqual(1, state.CompletedOperations);
        }
        finally
        {
            release.Set();
            injector.Callback = null;
            DeleteApprovedRoot(root);
        }
    }

    [TestMethod]
    public async Task Provision_DifferentCurrentContributorCanTakeOverAndRecoverPublishedPendingAttempt()
    {
        var root = CreateApprovedRoot();
        var injector = new TestProvisioningFailureInjector();
        try
        {
            using var factory = ProvisioningFactory(root, injector);
            using var originalOwner = await AuthenticatedClientAsync(factory);
            var setup = await ConfirmSetupAsync(originalOwner, "Cross actor crash recovery");
            using var recoveryContributor = await CreateProjectUserClientAsync(
                originalOwner,
                factory,
                setup.Project.ProjectId,
                "Member",
                "Contributor");

            var operationId = Guid.NewGuid();
            injector.FailurePoint = RepositoryProvisioningFailurePoint.AfterPublish;
            var interrupted = await originalOwner.PostAsJsonAsync(
                ProvisioningUrl(setup.Project.ProjectId),
                ProvisionPayload(setup, operationId));
            await AssertRejectedAsync(
                interrupted,
                HttpStatusCode.Conflict,
                RepositoryProvisioningInProgressException.ErrorCode);
            injector.FailurePoint = null;

            Guid attemptId;
            int recoveryActorUserId;
            await using (var connection = new SqlConnection(ConnectionString))
            {
                var pending = await ReadStateAsync(connection, setup.Project.ProjectId);
                Assert.IsNotNull(pending.LastAttemptId);
                attemptId = pending.LastAttemptId.Value;
                recoveryActorUserId = await connection.QuerySingleAsync<int>(
                    """
                    SELECT UserId FROM dbo.ProjectMembers
                    WHERE TenantId=1 AND ProjectId=@ProjectId AND UserId<>1
                      AND Status=N'Active' AND ProjectRole=N'Contributor';
                    """,
                    new { setup.Project.ProjectId });
                await connection.ExecuteAsync(
                    """
                    UPDATE dbo.ProjectMembers
                    SET Status=N'Removed', RemovedUtc=SYSUTCDATETIME(), RemovedByUserId=@RecoveryActorUserId
                    WHERE TenantId=1 AND ProjectId=@ProjectId AND UserId=1;
                    """,
                    new { setup.Project.ProjectId, RecoveryActorUserId = recoveryActorUserId });
            }

            var projected = await ReadRepositoryContextAsync(
                recoveryContributor,
                setup.Project.ProjectId);
            Assert.IsNotNull(projected.LatestProvisioning);
            Assert.AreEqual(attemptId, projected.LatestProvisioning.AttemptId);
            Assert.AreEqual(operationId, projected.LatestProvisioning.ClientOperationId);
            Assert.AreEqual(RepositoryProvisioningStates.Provisioning,
                projected.LatestProvisioning.State);

            var newFence = await RefreshFenceAsync(
                recoveryContributor,
                setup.Project.ProjectId);
            Assert.IsTrue(
                newFence.WorkbenchSessionId != setup.Project.WorkbenchSessionId ||
                newFence.LeaseEpoch != setup.Project.LeaseEpoch);
            var recovered = await ProvisionAsync(
                recoveryContributor,
                setup,
                operationId,
                workbenchSessionId: newFence.WorkbenchSessionId,
                leaseEpoch: newFence.LeaseEpoch);
            Assert.AreEqual(attemptId, recovered.AttemptId);
            Assert.AreEqual(
                await GitAsync(setup.Plan.TargetPath, "rev-parse", "HEAD"),
                recovered.BaselineCommit);

            await using (var connection = new SqlConnection(ConnectionString))
            {
                var state = await ReadStateAsync(connection, setup.Project.ProjectId);
                Assert.AreEqual(1, state.Attempts);
                Assert.AreEqual(1, state.Receipts);
                Assert.AreEqual(RepositoryBindingStates.Qualified, state.BindingState);
                var receiptActor = await connection.QuerySingleAsync<int>(
                    """
                    SELECT ActorUserId FROM dbo.RepositoryProvisioningReceipts
                    WHERE TenantId=1 AND ProjectId=@ProjectId;
                    """,
                    new { setup.Project.ProjectId });
                Assert.AreEqual(recoveryActorUserId, receiptActor,
                    "Cross-actor reconciliation must be attributed to the current contributor.");
                var completionActor = await connection.QuerySingleAsync<int>(
                    """
                    SELECT ActorUserId FROM dbo.UserMutationAttribution
                    WHERE TenantId=1 AND ProjectId=CONVERT(NVARCHAR(128), @ProjectId)
                      AND Route=N'/api/workbench/projects/{projectId}/repository/provisionings'
                      AND Phase=N'Completed';
                    """,
                    new { setup.Project.ProjectId });
                Assert.AreEqual(recoveryActorUserId, completionActor);
            }
        }
        finally
        {
            injector.FailurePoint = null;
            DeleteApprovedRoot(root);
        }
    }

    private static WebApplicationFactory<Program> ProvisioningFactory(
        string approvedRoot,
        TestProvisioningFailureInjector? injector = null,
        RecordingProvisioningGitRunner? gitRecorder = null) =>
        Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("WorkbenchV2:Enabled", "true");
            builder.UseSetting("WorkbenchRepositorySetup:ApprovedWorkspaceRoot", approvedRoot);
            builder.UseSetting("WorkbenchRepositoryProvisioning:GitExecutable", "git");
            builder.UseSetting("WorkbenchRepositoryProvisioning:GitTimeoutSeconds", "30");
            if (injector is not null)
            {
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IRepositoryProvisioningFailureInjector>();
                    services.AddSingleton<IRepositoryProvisioningFailureInjector>(injector);
                });
            }
            if (gitRecorder is not null)
            {
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IRepositoryProvisioningGitRunner>();
                    services.AddSingleton<IRepositoryProvisioningGitRunner>(provider =>
                    {
                        gitRecorder.Inner = new RepositoryProvisioningGitRunner(
                            provider.GetRequiredService<IConfiguration>());
                        return gitRecorder;
                    });
                });
            }
        });

    private static async Task<HttpClient> AuthenticatedClientAsync(
        WebApplicationFactory<Program> factory)
    {
        var token = await SelectTenantAsync(await LoginAsync());
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<ConfirmedSetup> ConfirmSetupAsync(HttpClient client, string name)
    {
        var startResponse = await client.PostAsJsonAsync(
            "/api/projects/start",
            new { clientOperationId = Guid.NewGuid(), name });
        Assert.AreEqual(HttpStatusCode.Created, startResponse.StatusCode,
            await startResponse.Content.ReadAsStringAsync());
        var start = await startResponse.Content.ReadFromJsonAsync<JsonElement>();
        var project = new StartedProject(
            start.GetProperty("projectId").GetInt32(),
            start.GetProperty("workbenchSessionId").GetInt64(),
            start.GetProperty("leaseEpoch").GetInt64());

        var planResponse = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{project.ProjectId}/repository/setup-plans",
            new
            {
                project.WorkbenchSessionId,
                project.LeaseEpoch,
                profileDefinitionId = ProfileId
            });
        Assert.AreEqual(HttpStatusCode.OK, planResponse.StatusCode,
            await planResponse.Content.ReadAsStringAsync());
        var plan = await planResponse.Content.ReadFromJsonAsync<RepositorySetupPlanPreview>();
        Assert.IsNotNull(plan);
        Assert.AreEqual(RepositorySetupPreviewStates.ReadyForConfirmation, plan.State);

        var confirmResponse = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{project.ProjectId}/repository/setup-confirmations",
            new
            {
                project.WorkbenchSessionId,
                project.LeaseEpoch,
                clientOperationId = Guid.NewGuid(),
                expectedPlanHash = plan.PlanHash
            });
        Assert.AreEqual(HttpStatusCode.OK, confirmResponse.StatusCode,
            await confirmResponse.Content.ReadAsStringAsync());
        var confirmation = await confirmResponse.Content
            .ReadFromJsonAsync<RepositorySetupConfirmationResult>();
        Assert.IsNotNull(confirmation);
        Assert.AreEqual(RepositoryBindingStates.SetupConfirmed,
            confirmation.RepositoryBinding.BindingState);
        Assert.AreEqual(ProjectExecutionReadinessStates.NotConfigured,
            confirmation.ExecutionReadiness);
        Assert.IsFalse(Directory.Exists(plan.TargetPath));
        return new ConfirmedSetup(project, plan, confirmation);
    }

    private static async Task<RepositoryProvisioningResult> ProvisionAsync(
        HttpClient client,
        ConfirmedSetup setup,
        Guid operationId,
        long? bindingRevision = null,
        long? profileRevision = null,
        long? workbenchSessionId = null,
        long? leaseEpoch = null)
    {
        var response = await client.PostAsJsonAsync(
            ProvisioningUrl(setup.Project.ProjectId),
            ProvisionPayload(
                setup,
                operationId,
                bindingRevision,
                profileRevision,
                workbenchSessionId: workbenchSessionId,
                leaseEpoch: leaseEpoch));
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
            await response.Content.ReadAsStringAsync());
        var result = await response.Content.ReadFromJsonAsync<RepositoryProvisioningResult>();
        Assert.IsNotNull(result);
        Assert.AreEqual(operationId, result.ClientOperationId);
        return result;
    }

    private static object ProvisionPayload(
        ConfirmedSetup setup,
        Guid operationId,
        long? bindingRevision = null,
        long? profileRevision = null,
        Guid? setupConfirmationId = null,
        long? workbenchSessionId = null,
        long? leaseEpoch = null) => new
    {
        workbenchSessionId = workbenchSessionId ?? setup.Project.WorkbenchSessionId,
        leaseEpoch = leaseEpoch ?? setup.Project.LeaseEpoch,
        clientOperationId = operationId,
        setupConfirmationId = setupConfirmationId ?? setup.Confirmation.ConfirmationId,
        expectedRepositoryBindingRevision = bindingRevision ??
                                            setup.Confirmation.RepositoryBinding.Revision,
        expectedExecutionProfileRevision = profileRevision ??
                                           setup.Confirmation.ExecutionProfile.Revision
    };

    private static async Task<StartedProject> RefreshFenceAsync(
        HttpClient client,
        int projectId)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{projectId}/open",
            new { clientOperationId = Guid.NewGuid(), takeOver = true });
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
            await response.Content.ReadAsStringAsync());
        var context = await response.Content.ReadFromJsonAsync<WorkbenchProjectEntryContext>();
        Assert.IsNotNull(context);
        Assert.AreEqual(projectId, context.ProjectId);
        return new StartedProject(
            context.ProjectId,
            context.WorkbenchSessionId,
            context.LeaseEpoch);
    }

    private static string ProvisioningUrl(int projectId) =>
        $"/api/workbench/projects/{projectId}/repository/provisionings";

    private static async Task<RepositorySetupContext> ReadRepositoryContextAsync(
        HttpClient client,
        int projectId)
    {
        var response = await client.GetAsync($"/api/workbench/projects/{projectId}/repository");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
            await response.Content.ReadAsStringAsync());
        var context = await response.Content.ReadFromJsonAsync<RepositorySetupContext>();
        Assert.IsNotNull(context);
        return context;
    }

    private static async Task AssertRejectedAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedError)
    {
        Assert.AreEqual(expectedStatus, response.StatusCode,
            await response.Content.ReadAsStringAsync());
        await AssertErrorAsync(response, expectedError);
    }

    private static async Task AssertErrorAsync(HttpResponseMessage response, string expectedError)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual(expectedError, body.GetProperty("error").GetString());
    }

    private static async Task AssertProvisioningErrorAsync(
        HttpResponseMessage response,
        string expectedError,
        string expectedReasonCode)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual(expectedError, body.GetProperty("error").GetString());
        Assert.AreEqual(expectedReasonCode, body.GetProperty("reasonCode").GetString());
        Assert.IsTrue(body.TryGetProperty("message", out var message));
        Assert.IsFalse(string.IsNullOrWhiteSpace(message.GetString()));
    }

    private static Task<ProvisioningState> ReadStateAsync(
        SqlConnection connection,
        int projectId) => connection.QuerySingleAsync<ProvisioningState>(
        """
        SELECT
            (SELECT COUNT(1) FROM dbo.RepositoryProvisioningAttempts
             WHERE TenantId=1 AND ProjectId=@ProjectId) AS Attempts,
            (SELECT COUNT(1) FROM dbo.RepositoryProvisioningAttempts
             WHERE TenantId=1 AND ProjectId=@ProjectId AND State=N'Provisioning') AS ProvisioningAttempts,
            (SELECT COUNT(1) FROM dbo.RepositoryProvisioningAttempts
             WHERE TenantId=1 AND ProjectId=@ProjectId AND State=N'Qualified') AS QualifiedAttempts,
            (SELECT COUNT(1) FROM dbo.RepositoryProvisioningAttempts
             WHERE TenantId=1 AND ProjectId=@ProjectId AND State=N'ProvisioningFailed') AS FailedAttempts,
            (SELECT COUNT(1) FROM dbo.RepositoryProvisioningReceipts
             WHERE TenantId=1 AND ProjectId=@ProjectId) AS Receipts,
            (SELECT COUNT(1) FROM dbo.ClientOperations
             WHERE TenantId=1 AND OperationKind=N'ProvisionRepository'
               AND ResourceScopeId=CONCAT(N'project:', @ProjectId, N':repository-provisioning')
               AND Status=N'Pending') AS PendingOperations,
            (SELECT COUNT(1) FROM dbo.ClientOperations
             WHERE TenantId=1 AND OperationKind=N'ProvisionRepository'
               AND ResourceScopeId=CONCAT(N'project:', @ProjectId, N':repository-provisioning')
               AND Status=N'Completed') AS CompletedOperations,
            (SELECT COUNT(1) FROM dbo.ClientOperations
             WHERE TenantId=1 AND OperationKind=N'ProvisionRepository'
               AND ResourceScopeId=CONCAT(N'project:', @ProjectId, N':repository-provisioning')
               AND Status=N'Failed') AS FailedOperations,
            (SELECT COUNT(1) FROM dbo.UserMutationAttribution
             WHERE TenantId=1 AND ProjectId=CONVERT(NVARCHAR(128), @ProjectId)
               AND Route=N'/api/workbench/projects/{projectId}/repository/provisionings'
               AND Phase=N'Attempted') AS AttemptedAttributions,
            (SELECT COUNT(1) FROM dbo.UserMutationAttribution
             WHERE TenantId=1 AND ProjectId=CONVERT(NVARCHAR(128), @ProjectId)
               AND Route=N'/api/workbench/projects/{projectId}/repository/provisionings'
               AND Phase=N'Completed') AS CompletedAttributions,
            (SELECT COUNT(1) FROM dbo.UserMutationAttribution
             WHERE TenantId=1 AND ProjectId=CONVERT(NVARCHAR(128), @ProjectId)
               AND Route=N'/api/workbench/projects/{projectId}/repository/provisionings'
               AND Phase=N'Failed') AS FailedAttributions,
            (SELECT TOP (1) Id FROM dbo.RepositoryProvisioningAttempts
             WHERE TenantId=1 AND ProjectId=@ProjectId ORDER BY AttemptNumber DESC) AS LastAttemptId,
            (SELECT TOP (1) StagingPath FROM dbo.RepositoryProvisioningAttempts
             WHERE TenantId=1 AND ProjectId=@ProjectId ORDER BY AttemptNumber DESC) AS LastStagingPath,
            (SELECT TOP (1) FailureCode FROM dbo.RepositoryProvisioningAttempts
             WHERE TenantId=1 AND ProjectId=@ProjectId ORDER BY AttemptNumber DESC) AS LastFailureCode,
            (SELECT TOP (1) FailureEvidenceJson FROM dbo.RepositoryProvisioningAttempts
             WHERE TenantId=1 AND ProjectId=@ProjectId ORDER BY AttemptNumber DESC) AS LastFailureEvidenceJson,
            (SELECT BindingState FROM dbo.RepositoryBindings
             WHERE TenantId=1 AND ProjectId=@ProjectId) AS BindingState,
            (SELECT CurrentRevision FROM dbo.RepositoryBindings
             WHERE TenantId=1 AND ProjectId=@ProjectId) AS BindingRevision,
            (SELECT DefaultBranch FROM dbo.RepositoryBindings
             WHERE TenantId=1 AND ProjectId=@ProjectId) AS DefaultBranch,
            (SELECT BaselineCommit FROM dbo.RepositoryBindings
             WHERE TenantId=1 AND ProjectId=@ProjectId) AS BaselineCommit,
            (SELECT CurrentRevision FROM dbo.ProjectExecutionProfiles
             WHERE TenantId=1 AND ProjectId=@ProjectId) AS ExecutionProfileRevision,
            (SELECT LocalPath FROM dbo.Projects
             WHERE TenantId=1 AND Id=@ProjectId) AS LocalPath,
            (SELECT TOP (1) ExecutionReadiness FROM dbo.ProjectReadinessAssessments
             WHERE TenantId=1 AND ProjectId=@ProjectId ORDER BY Revision DESC) AS ExecutionReadiness,
            (SELECT TOP (1) Phase FROM dbo.ProjectLifecyclePhases
             WHERE TenantId=1 AND ProjectId=@ProjectId ORDER BY Revision DESC) AS LifecyclePhase,
            (SELECT COUNT(1) FROM dbo.ProjectFiles WHERE ProjectId=@ProjectId) AS ProjectFiles,
            (SELECT COUNT(1) FROM dbo.CodeIndexEntries
             WHERE TenantId=1 AND ProjectId=@ProjectId) AS CodeIndexEntries,
            (SELECT COUNT(1) FROM dbo.ProjectCommands
             WHERE TenantId=1 AND ProjectId=@ProjectId) AS ProjectCommands,
            (SELECT COUNT(1) FROM dbo.ProjectProfiles
             WHERE TenantId=1 AND ProjectId=@ProjectId) AS ProjectProfiles,
            (SELECT COUNT(1) FROM dbo.Runs WHERE ProjectId=@ProjectId) AS Runs;
        """,
        new { ProjectId = projectId });

    private static Task<string> ReadForeignKeyColumnsAsync(
        SqlConnection connection,
        string foreignKeyName) => connection.QuerySingleAsync<string>(
        """
        SELECT STRING_AGG(
                   CONVERT(NVARCHAR(MAX), COL_NAME(columns.parent_object_id, columns.parent_column_id)),
                   N',') WITHIN GROUP (ORDER BY columns.constraint_column_id)
        FROM sys.foreign_keys foreignKey
        INNER JOIN sys.foreign_key_columns columns
            ON columns.constraint_object_id=foreignKey.object_id
        WHERE foreignKey.name=@ForeignKeyName;
        """,
        new { ForeignKeyName = foreignKeyName });

    private static async Task AssertSqlConstraintRejectedAsync(
        SqlConnection connection,
        string sql,
        object parameters,
        string expectedConstraint)
    {
        using var transaction = connection.BeginTransaction();
        SqlException? rejection = null;
        try
        {
            await connection.ExecuteAsync(sql, parameters, transaction);
        }
        catch (SqlException exception)
        {
            rejection = exception;
        }
        finally
        {
            try
            {
                transaction.Rollback();
            }
            catch (InvalidOperationException)
            {
            }
        }

        Assert.IsNotNull(rejection, $"SQL unexpectedly accepted a tuple guarded by {expectedConstraint}.");
        Assert.AreEqual(547, rejection.Number);
        StringAssert.Contains(rejection.Message, expectedConstraint);
    }

    private static Task<string> ReadMigrationCountsAsync(SqlConnection connection) =>
        connection.QuerySingleAsync<string>(
            """
            SELECT
                JSON_QUERY((SELECT Id, ProjectId, PlanHash FROM dbo.RepositorySetupConfirmations
                            ORDER BY ProjectId FOR JSON PATH)) AS confirmations,
                JSON_QUERY((SELECT Id, ProjectId, AttemptNumber, State, PlanHash
                            FROM dbo.RepositoryProvisioningAttempts
                            ORDER BY ProjectId, AttemptNumber FOR JSON PATH)) AS attempts,
                JSON_QUERY((SELECT Id, ProjectId, ProvisioningAttemptId, BaselineCommit,
                                   ManifestSha256, GitTreeId, ReceiptSha256
                            FROM dbo.RepositoryProvisioningReceipts
                            ORDER BY ProjectId FOR JSON PATH)) AS receipts,
                JSON_QUERY((SELECT Id, ProjectId, CurrentRevision, BindingState,
                                   DefaultBranch, BaselineCommit
                            FROM dbo.RepositoryBindings
                            ORDER BY ProjectId FOR JSON PATH)) AS bindings
            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
            """);

    private static async Task ApplyProvisioningMigrationAsync(SqlConnection connection)
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "Database",
            "migrate_workbench_repository_provisioning.sql");
        var sql = await File.ReadAllTextAsync(path);
        Assert.IsFalse(Regex.IsMatch(sql, @"(?im)^\s*USE\s"),
            "The provisioning migration must remain catalog-agnostic.");
        foreach (var batch in Regex.Split(
                     sql.Replace("\r\n", "\n", StringComparison.Ordinal),
                     @"(?im)^\s*GO\s*$")
                 .Select(value => value.Trim())
                 .Where(value => value.Length > 0))
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
        throw new DirectoryNotFoundException("Could not locate the IronDev repository root.");
    }

    private static async Task<string> GitAsync(string repository, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = repository,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        startInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        startInfo.Environment["GIT_CONFIG_GLOBAL"] = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GCM_INTERACTIVE"] = "never";

        using var process = new Process { StartInfo = startInfo };
        Assert.IsTrue(process.Start());
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await process.WaitForExitAsync(timeout.Token);
        var error = await stderr;
        Assert.AreEqual(0, process.ExitCode,
            $"git {string.Join(' ', arguments)} failed: {error}");
        return (await stdout).Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');
    }

    private static void AssertLowerHex(string value, int length) =>
        Assert.IsTrue(
            value.Length == length &&
            value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f'),
            $"Expected a lowercase {length}-hex value, got '{value}'.");

    private static bool IsJson(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        try
        {
            using var _ = JsonDocument.Parse(value);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string CreateApprovedRoot()
    {
        var drive = Path.GetPathRoot(Environment.SystemDirectory)
                    ?? throw new InvalidOperationException("A local system drive is required.");
        var root = Path.Combine(
            drive,
            "IronDev.RepositoryProvisioning.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteApprovedRoot(string root)
    {
        if (!Directory.Exists(root))
            return;
        var safeBase = Path.GetFullPath(Path.Combine(
            Path.GetPathRoot(Environment.SystemDirectory)!,
            "IronDev.RepositoryProvisioning.Tests"));
        var exact = Path.GetFullPath(root);
        if (!exact.StartsWith(safeBase + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(exact, safeBase, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to clean a non-test provisioning root.");
        if (File.GetAttributes(exact).HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidOperationException("Refusing to clean a reparse-point test root.");
        foreach (var entry in Directory.EnumerateFileSystemEntries(exact, "*", SearchOption.AllDirectories))
        {
            var attributes = File.GetAttributes(entry);
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidOperationException(
                    "Refusing to clean a test root containing a reparse point.");
            if (!attributes.HasFlag(FileAttributes.Directory) &&
                attributes.HasFlag(FileAttributes.ReadOnly))
                File.SetAttributes(entry, attributes & ~FileAttributes.ReadOnly);
        }
        Directory.Delete(exact, recursive: true);
    }

    private string SeedAttemptStaging(
        int projectId,
        RepositorySetupPlanPreview plan,
        bool exactMarker)
    {
        using var connection = new SqlConnection(ConnectionString);
        var attempt = connection.QuerySingle<AttemptSeed>(
            """
            SELECT TOP (1) Id, StagingPath, PlanHash, CanonicalTargetPath
            FROM dbo.RepositoryProvisioningAttempts
            WHERE TenantId=1 AND ProjectId=@ProjectId
            ORDER BY AttemptNumber DESC;
            """,
            new { ProjectId = projectId });
        Assert.AreEqual(plan.PlanHash, attempt.PlanHash);
        Directory.CreateDirectory(attempt.StagingPath);
        if (!exactMarker)
        {
            File.WriteAllText(
                Path.Combine(attempt.StagingPath, "foreign-owner.txt"),
                "this staging path is not owned by the attempt\n",
                new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(attempt.StagingPath, ".irondev-provisioning-attempt.json"),
                "{\"attemptId\":\"mismatch\"}\n",
                new UTF8Encoding(false));
            return attempt.StagingPath;
        }

        var marker = RepositorySetupCanonicalJson.Serialize(new
        {
            schemaVersion = 1,
            attemptId = attempt.Id,
            planHash = attempt.PlanHash,
            targetPathSha256 = RepositorySetupCanonicalJson.Sha256(attempt.CanonicalTargetPath)
        });
        File.WriteAllText(
            Path.Combine(attempt.StagingPath, ".irondev-provisioning-attempt.json"),
            marker + "\n",
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(attempt.StagingPath, "partial-owned-file.txt"),
            "safe to replace\n",
            new UTF8Encoding(false));
        return attempt.StagingPath;
    }

    private static async Task<HttpClient> CreateProjectUserClientAsync(
        HttpClient owner,
        WebApplicationFactory<Program> factory,
        int? projectId,
        string tenantRole,
        string projectRole)
    {
        var email = $"repository-provisioner-{Guid.NewGuid():N}@irondev.local";
        const string password = "repository-provisioning-boundary-password";
        var create = await owner.PostAsJsonAsync(
            $"/api/tenants/{AssignedTenantId}/users",
            new
            {
                email,
                displayName = "Repository Provisioning Boundary User",
                password,
                role = tenantRole
            });
        Assert.AreEqual(HttpStatusCode.OK, create.StatusCode,
            await create.Content.ReadAsStringAsync());
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        if (projectId.HasValue)
        {
            var membership = await owner.PutAsJsonAsync(
                $"/api/projects/{projectId.Value}/members/{created.GetProperty("id").GetInt32()}",
                new { projectRole });
            Assert.AreEqual(HttpStatusCode.OK, membership.StatusCode,
                await membership.Content.ReadAsStringAsync());
        }

        var token = await SelectTenantAsync(await LoginAsync(email, password));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private void ApplyFinalizeFenceMutation(
        StartedProject project,
        FinalizeFenceMutation mutation)
    {
        using var connection = new SqlConnection(ConnectionString);
        connection.Open();
        switch (mutation)
        {
            case FinalizeFenceMutation.TenantMembership:
                connection.Execute(
                    "DELETE FROM dbo.TenantUsers WHERE TenantId=1 AND UserId=1;");
                break;
            case FinalizeFenceMutation.ProjectMembership:
                connection.Execute(
                    """
                    UPDATE dbo.ProjectMembers
                    SET Status=N'Removed', RemovedUtc=SYSUTCDATETIME(), RemovedByUserId=1
                    WHERE TenantId=1 AND ProjectId=@ProjectId AND UserId=1;
                    """,
                    new { project.ProjectId });
                break;
            case FinalizeFenceMutation.ProjectRole:
                connection.Execute(
                    """
                    UPDATE dbo.ProjectMembers SET ProjectRole=N'Viewer'
                    WHERE TenantId=1 AND ProjectId=@ProjectId AND UserId=1;
                    """,
                    new { project.ProjectId });
                break;
            case FinalizeFenceMutation.Session:
                connection.Execute(
                    """
                    UPDATE dbo.WorkbenchSessions SET Status=N'Historical', ClosedAtUtc=SYSUTCDATETIME()
                    WHERE TenantId=1 AND ProjectId=@ProjectId AND Id=@WorkbenchSessionId;
                    """,
                    project);
                break;
            case FinalizeFenceMutation.Holder:
                var otherUserId = connection.QuerySingle<int>(
                    """
                    INSERT dbo.Users(Email, DisplayName, IsActive)
                    OUTPUT inserted.Id
                    VALUES (@Email, N'Finalize Fence Holder', 1);
                    """,
                    new { Email = $"finalize-holder-{project.ProjectId}@irondev.local" });
                connection.Execute(
                    """
                    UPDATE dbo.WorkbenchWriteLeases SET HolderActorUserId=@OtherUserId
                    WHERE TenantId=1 AND ProjectId=@ProjectId
                      AND WorkbenchSessionId=@WorkbenchSessionId AND LeaseEpoch=@LeaseEpoch;
                    """,
                    new
                    {
                        OtherUserId = otherUserId,
                        project.ProjectId,
                        project.WorkbenchSessionId,
                        project.LeaseEpoch
                    });
                break;
            case FinalizeFenceMutation.RevokedEpoch:
                connection.Execute(
                    """
                    UPDATE dbo.WorkbenchWriteLeases SET RevokedAtUtc=SYSUTCDATETIME()
                    WHERE TenantId=1 AND ProjectId=@ProjectId
                      AND WorkbenchSessionId=@WorkbenchSessionId AND LeaseEpoch=@LeaseEpoch;
                    """,
                    project);
                break;
            case FinalizeFenceMutation.Expiry:
                connection.Execute(
                    """
                    UPDATE dbo.WorkbenchWriteLeases
                    SET ExpiresAtUtc=DATEADD(MINUTE, -1, SYSUTCDATETIME())
                    WHERE TenantId=1 AND ProjectId=@ProjectId
                      AND WorkbenchSessionId=@WorkbenchSessionId AND LeaseEpoch=@LeaseEpoch;
                    """,
                    project);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null);
        }
    }

    private void RestoreFinalizeFence(
        StartedProject project,
        FinalizeFenceMutation mutation)
    {
        using var connection = new SqlConnection(ConnectionString);
        connection.Open();
        switch (mutation)
        {
            case FinalizeFenceMutation.TenantMembership:
                connection.Execute(
                    """
                    IF NOT EXISTS (SELECT 1 FROM dbo.TenantUsers WHERE TenantId=1 AND UserId=1)
                        INSERT dbo.TenantUsers(TenantId, UserId, Role) VALUES (1, 1, N'Owner');
                    """);
                break;
            case FinalizeFenceMutation.ProjectMembership:
                connection.Execute(
                    """
                    UPDATE dbo.ProjectMembers
                    SET Status=N'Active', RemovedUtc=NULL, RemovedByUserId=NULL, ProjectRole=N'Owner'
                    WHERE TenantId=1 AND ProjectId=@ProjectId AND UserId=1;
                    """,
                    new { project.ProjectId });
                break;
            case FinalizeFenceMutation.ProjectRole:
                connection.Execute(
                    """
                    UPDATE dbo.ProjectMembers SET ProjectRole=N'Owner'
                    WHERE TenantId=1 AND ProjectId=@ProjectId AND UserId=1;
                    """,
                    new { project.ProjectId });
                break;
            case FinalizeFenceMutation.Session:
                connection.Execute(
                    """
                    UPDATE dbo.WorkbenchSessions SET Status=N'Active', ClosedAtUtc=NULL
                    WHERE TenantId=1 AND ProjectId=@ProjectId AND Id=@WorkbenchSessionId;
                    """,
                    project);
                break;
            case FinalizeFenceMutation.Holder:
                connection.Execute(
                    """
                    UPDATE dbo.WorkbenchWriteLeases SET HolderActorUserId=1
                    WHERE TenantId=1 AND ProjectId=@ProjectId
                      AND WorkbenchSessionId=@WorkbenchSessionId AND LeaseEpoch=@LeaseEpoch;
                    DELETE FROM dbo.Users WHERE Email=@Email;
                    """,
                    new
                    {
                        project.ProjectId,
                        project.WorkbenchSessionId,
                        project.LeaseEpoch,
                        Email = $"finalize-holder-{project.ProjectId}@irondev.local"
                    });
                break;
            case FinalizeFenceMutation.RevokedEpoch:
                connection.Execute(
                    """
                    UPDATE dbo.WorkbenchWriteLeases SET RevokedAtUtc=NULL
                    WHERE TenantId=1 AND ProjectId=@ProjectId
                      AND WorkbenchSessionId=@WorkbenchSessionId AND LeaseEpoch=@LeaseEpoch;
                    """,
                    project);
                break;
            case FinalizeFenceMutation.Expiry:
                connection.Execute(
                    """
                    UPDATE dbo.WorkbenchWriteLeases
                    SET ExpiresAtUtc=DATEADD(MINUTE, 30, SYSUTCDATETIME())
                    WHERE TenantId=1 AND ProjectId=@ProjectId
                      AND WorkbenchSessionId=@WorkbenchSessionId AND LeaseEpoch=@LeaseEpoch;
                    """,
                    project);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null);
        }
    }

    private static HttpStatusCode ExpectedFinalizeFenceStatus(FinalizeFenceMutation mutation) =>
        mutation switch
        {
            FinalizeFenceMutation.TenantMembership => HttpStatusCode.NotFound,
            FinalizeFenceMutation.ProjectMembership => HttpStatusCode.NotFound,
            FinalizeFenceMutation.ProjectRole => HttpStatusCode.Forbidden,
            _ => HttpStatusCode.Conflict
        };

    private sealed record StartedProject(
        int ProjectId,
        long WorkbenchSessionId,
        long LeaseEpoch);

    private sealed record ConfirmedSetup(
        StartedProject Project,
        RepositorySetupPlanPreview Plan,
        RepositorySetupConfirmationResult Confirmation);

    private sealed class ProvisioningState
    {
        public int Attempts { get; init; }
        public int ProvisioningAttempts { get; init; }
        public int QualifiedAttempts { get; init; }
        public int FailedAttempts { get; init; }
        public int Receipts { get; init; }
        public int PendingOperations { get; init; }
        public int CompletedOperations { get; init; }
        public int FailedOperations { get; init; }
        public int AttemptedAttributions { get; init; }
        public int CompletedAttributions { get; init; }
        public int FailedAttributions { get; init; }
        public Guid? LastAttemptId { get; init; }
        public string? LastStagingPath { get; init; }
        public string? LastFailureCode { get; init; }
        public string? LastFailureEvidenceJson { get; init; }
        public string? BindingState { get; init; }
        public long BindingRevision { get; init; }
        public string? DefaultBranch { get; init; }
        public string? BaselineCommit { get; init; }
        public long ExecutionProfileRevision { get; init; }
        public string? LocalPath { get; init; }
        public string ExecutionReadiness { get; init; } = string.Empty;
        public string LifecyclePhase { get; init; } = string.Empty;
        public int ProjectFiles { get; init; }
        public int CodeIndexEntries { get; init; }
        public int ProjectCommands { get; init; }
        public int ProjectProfiles { get; init; }
        public int Runs { get; init; }
    }

    private sealed class AttemptSeed
    {
        public Guid Id { get; init; }
        public string StagingPath { get; init; } = string.Empty;
        public string PlanHash { get; init; } = string.Empty;
        public string CanonicalTargetPath { get; init; } = string.Empty;
    }

    private sealed class HistoricalFenceState
    {
        public string OldSessionStatus { get; init; } = string.Empty;
        public DateTime? OldLeaseRevokedAtUtc { get; init; }
        public string NewSessionStatus { get; init; } = string.Empty;
        public int CurrentLeaseRows { get; init; }
    }

    private sealed class ProvisioningReceiptTimes
    {
        public DateTime PublishedAtUtc { get; init; }
        public DateTime RecordedAtUtc { get; init; }
        public string ReceiptJson { get; init; } = string.Empty;
    }

    private sealed class TestProvisioningFailureInjector : IRepositoryProvisioningFailureInjector
    {
        public RepositoryProvisioningFailurePoint? FailurePoint { get; set; }
        public Action<RepositoryProvisioningFailurePoint>? Callback { get; set; }

        public void ThrowIfRequested(RepositoryProvisioningFailurePoint point)
        {
            Callback?.Invoke(point);
            if (FailurePoint == point)
                throw new InjectedProvisioningFailureException(point);
        }
    }

    private sealed class RecordingProvisioningGitRunner : IRepositoryProvisioningGitRunner
    {
        private readonly object _gate = new();
        private readonly List<string> _failures = [];

        public IRepositoryProvisioningGitRunner? Inner { get; set; }

        public async Task<RepositoryProvisioningGitResult> RunAsync(
            string repositoryPath,
            IReadOnlyList<string> arguments,
            DateTime deterministicCommitTimeUtc,
            CancellationToken cancellationToken = default)
        {
            var inner = Inner ?? throw new InvalidOperationException("Git recorder was not initialized.");
            var result = await inner.RunAsync(
                repositoryPath,
                arguments,
                deterministicCommitTimeUtc,
                cancellationToken);
            if (result.ExitCode != 0)
            {
                lock (_gate)
                {
                    _failures.Add(
                        $"git {string.Join(' ', arguments)} => {result.ExitCode}; " +
                        $"stderr={result.StandardError}; stdout={result.StandardOutput}");
                }
            }
            return result;
        }

        public string DescribeLastFailure()
        {
            lock (_gate)
                return _failures.Count == 0 ? "No failing Git command was recorded." : _failures[^1];
        }
    }

    private sealed class InjectedProvisioningFailureException(
        RepositoryProvisioningFailurePoint point)
        : Exception($"Injected repository provisioning failure at {point}.");

    private enum FinalizeFenceMutation
    {
        TenantMembership,
        ProjectMembership,
        ProjectRole,
        Session,
        Holder,
        RevokedEpoch,
        Expiry
    }
}
