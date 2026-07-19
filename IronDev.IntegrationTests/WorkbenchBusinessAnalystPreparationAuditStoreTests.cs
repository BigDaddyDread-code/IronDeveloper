using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using IronDev.Core.Interfaces;
using IronDev.Core.Workbench;
using IronDev.Data;
using IronDev.Infrastructure.Services;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class WorkbenchBusinessAnalystPreparationAuditStoreTests : IntegrationTestBase
{
    private static readonly string[] SnapshotToolNames =
    [
        "workbench.project-identity.read",
        "workbench.captured-understanding.read",
        "workbench.bounded-trusted-conversation.read"
    ];

    [TestInitialize]
    public override async Task TestInitialize()
    {
        await base.TestInitialize();
        await ApplyMigrationAsync("migrate_user_mutation_attribution.sql");
        await ApplyMigrationAsync("migrate_workbench_project_start.sql");
        await ApplyMigrationAsync("migrate_workbench_agent_runs.sql");
        await ApplyMigrationAsync("migrate_workbench_ba_preparation_audit.sql");
    }

    [TestMethod]
    public async Task Record_IsAtomicAppendOnlyAndIdenticalReplayIsIdempotent()
    {
        var claim = await CreateClaimAsync("Durable Analyst preparation");
        var provenance = CreateProvenance(claim);
        var store = CreateStore();

        var first = await store.RecordAsync(provenance);
        var replay = await store.RecordAsync(provenance);

        Assert.AreEqual(WorkbenchBusinessAnalystPreparationWriteStatus.Recorded, first.Status);
        Assert.AreEqual(WorkbenchBusinessAnalystPreparationWriteStatus.AlreadyExists, replay.Status);
        Assert.AreEqual(first.PreparationHash, replay.PreparationHash);
        CollectionAssert.AreEquivalent(
            first.ToolCallHashes.OrderBy(value => value.Key).ToArray(),
            replay.ToolCallHashes.OrderBy(value => value.Key).ToArray());

        await using var connection = new SqlConnection(ConnectionString);
        var state = await connection.QuerySingleAsync<AuditState>("""
            SELECT
                (SELECT COUNT(1) FROM dbo.WorkbenchBusinessAnalystPreparations
                 WHERE AgentRunId=@AgentRunId AND ClaimToken=@ClaimToken AND AttemptNumber=@AttemptNumber) AS Preparations,
                (SELECT COUNT(1) FROM dbo.WorkbenchBusinessAnalystToolCallAudits
                 WHERE AgentRunId=@AgentRunId AND ClaimToken=@ClaimToken AND AttemptNumber=@AttemptNumber) AS ToolCalls,
                (SELECT TOP (1) ActualProvider FROM dbo.WorkbenchBusinessAnalystPreparations
                 WHERE AgentRunId=@AgentRunId AND ClaimToken=@ClaimToken AND AttemptNumber=@AttemptNumber) AS ActualProvider,
                (SELECT TOP (1) ActualModel FROM dbo.WorkbenchBusinessAnalystPreparations
                 WHERE AgentRunId=@AgentRunId AND ClaimToken=@ClaimToken AND AttemptNumber=@AttemptNumber) AS ActualModel,
                (SELECT TOP (1) ProviderTimeoutSeconds FROM dbo.WorkbenchBusinessAnalystPreparations
                 WHERE AgentRunId=@AgentRunId AND ClaimToken=@ClaimToken AND AttemptNumber=@AttemptNumber) AS ProviderTimeoutSeconds;
            """, new { claim.AgentRunId, claim.ClaimToken, AttemptNumber = claim.AttemptCount });
        Assert.AreEqual(1, state.Preparations);
        Assert.AreEqual(3, state.ToolCalls);
        Assert.AreEqual("openai", state.ActualProvider);
        Assert.AreEqual("gpt-5.2", state.ActualModel);
        Assert.AreEqual(45, state.ProviderTimeoutSeconds);

        var forbiddenPayloadColumns = await connection.ExecuteScalarAsync<int>("""
            SELECT COUNT(1)
            FROM sys.columns
            WHERE object_id IN
                (OBJECT_ID(N'dbo.WorkbenchBusinessAnalystPreparations'),
                 OBJECT_ID(N'dbo.WorkbenchBusinessAnalystToolCallAudits'))
              AND name IN
                (N'RawPrompt', N'PromptText', N'RawToolInput', N'RawToolOutput',
                 N'ToolInputJson', N'ToolOutputJson', N'RawModelOutput', N'ChainOfThought');
            """);
        Assert.AreEqual(0, forbiddenPayloadColumns);

        var appendOnly = await Assert.ThrowsExactlyAsync<SqlException>(() => connection.ExecuteAsync("""
            UPDATE dbo.WorkbenchBusinessAnalystPreparations
            SET ActualModel=N'changed-model'
            WHERE AgentRunId=@AgentRunId;
            """, new { claim.AgentRunId }));
        StringAssert.Contains(appendOnly.Message, "append-only");
    }

    [TestMethod]
    public async Task Record_RejectsMismatchedAttemptAndDifferentReplayWithoutPartialWrites()
    {
        var claim = await CreateClaimAsync("Fenced Analyst preparation");
        var provenance = CreateProvenance(claim);
        var store = CreateStore();

        await Assert.ThrowsExactlyAsync<WorkbenchBusinessAnalystPreparationAuditConflictException>(() =>
            store.RecordAsync(provenance with { ClaimToken = Guid.NewGuid() }));
        await Assert.ThrowsExactlyAsync<WorkbenchBusinessAnalystPreparationAuditConflictException>(() =>
            store.RecordAsync(provenance with { AttemptNumber = claim.AttemptCount + 1 }));

        var first = await store.RecordAsync(provenance);
        await Assert.ThrowsExactlyAsync<WorkbenchBusinessAnalystPreparationAuditConflictException>(() =>
            store.RecordAsync(provenance with { ActualModel = "gpt-5.3" }));

        var unsafeCalls = provenance.ToolCalls.ToArray();
        unsafeCalls[0] = unsafeCalls[0] with { SafeSummary = "raw prompt: do not persist this" };
        await Assert.ThrowsExactlyAsync<WorkbenchBusinessAnalystPreparationAuditValidationException>(() =>
            store.RecordAsync(provenance with { ToolCalls = unsafeCalls }));

        await using var connection = new SqlConnection(ConnectionString);
        var counts = await connection.QuerySingleAsync<AuditCounts>("""
            SELECT
                (SELECT COUNT(1) FROM dbo.WorkbenchBusinessAnalystPreparations
                 WHERE AgentRunId=@AgentRunId) AS Preparations,
                (SELECT COUNT(1) FROM dbo.WorkbenchBusinessAnalystToolCallAudits
                 WHERE AgentRunId=@AgentRunId) AS ToolCalls;
            """, new { claim.AgentRunId });
        Assert.AreEqual(1, counts.Preparations);
        Assert.AreEqual(3, counts.ToolCalls);
        Assert.AreEqual(64, first.PreparationHash.Length);
    }

    [TestMethod]
    public async Task Record_RejectsPolicyDefinitionToolSetAndSuccessfulStatusDrift()
    {
        var claim = await CreateClaimAsync("Pinned Analyst preparation contract");
        var provenance = CreateProvenance(claim);
        var store = CreateStore();

        await Assert.ThrowsExactlyAsync<WorkbenchBusinessAnalystPreparationAuditConflictException>(() =>
            store.RecordAsync(provenance with { ToolManifestHash = Hash("untrusted-tool-manifest") }));

        var policyDrift = provenance.ToolCalls.ToArray();
        policyDrift[0] = policyDrift[0] with { PolicyVersion = "workbench-ba-readonly-v999" };
        await Assert.ThrowsExactlyAsync<WorkbenchBusinessAnalystPreparationAuditConflictException>(() =>
            store.RecordAsync(provenance with { ToolCalls = policyDrift }));

        var definitionDrift = provenance.ToolCalls.ToArray();
        definitionDrift[0] = definitionDrift[0] with { DefinitionVersion = "snapshot-v999" };
        await Assert.ThrowsExactlyAsync<WorkbenchBusinessAnalystPreparationAuditConflictException>(() =>
            store.RecordAsync(provenance with { ToolCalls = definitionDrift }));

        await Assert.ThrowsExactlyAsync<WorkbenchBusinessAnalystPreparationAuditConflictException>(() =>
            store.RecordAsync(provenance with { ToolCalls = provenance.ToolCalls.Skip(1).ToArray() }));

        var extraTool = provenance.ToolCalls.Concat(
        [
            provenance.ToolCalls[0] with { ToolName = "workbench.unregistered.read" }
        ]).ToArray();
        await Assert.ThrowsExactlyAsync<WorkbenchBusinessAnalystPreparationAuditConflictException>(() =>
            store.RecordAsync(provenance with { ToolCalls = extraTool }));

        var statusDrift = provenance.ToolCalls.ToArray();
        statusDrift[0] = statusDrift[0] with
        {
            Status = WorkbenchBusinessAnalystToolCallAuditStatus.Rejected
        };
        await Assert.ThrowsExactlyAsync<WorkbenchBusinessAnalystPreparationAuditConflictException>(() =>
            store.RecordAsync(provenance with { ToolCalls = statusDrift }));

        await using var connection = new SqlConnection(ConnectionString);
        var counts = await connection.QuerySingleAsync<AuditCounts>("""
            SELECT
                (SELECT COUNT(1) FROM dbo.WorkbenchBusinessAnalystPreparations
                 WHERE AgentRunId=@AgentRunId) AS Preparations,
                (SELECT COUNT(1) FROM dbo.WorkbenchBusinessAnalystToolCallAudits
                 WHERE AgentRunId=@AgentRunId) AS ToolCalls;
            """, new { claim.AgentRunId });
        Assert.AreEqual(0, counts.Preparations);
        Assert.AreEqual(0, counts.ToolCalls);
    }

    [TestMethod]
    public async Task Record_RejectsUnsupportedDurableRunContractTuple()
    {
        var claim = await CreateClaimAsync("Unsupported pinned Analyst contract");
        var provenance = CreateProvenance(claim);
        await using (var connection = new SqlConnection(ConnectionString))
        {
            await connection.ExecuteAsync("""
                UPDATE dbo.WorkbenchAgentRuns
                SET ToolPolicyVersion=N'workbench-ba-readonly-v999'
                WHERE AgentRunId=@AgentRunId;
                """, new { claim.AgentRunId });
        }

        await Assert.ThrowsExactlyAsync<WorkbenchBusinessAnalystPreparationAuditConflictException>(() =>
            CreateStore().RecordAsync(provenance));
    }

    private async Task<WorkbenchAgentRunClaim> CreateClaimAsync(string projectName)
    {
        var actorUserId = await SeedActorAsync();
        var start = await new ProjectStartService(ConnectionFactory(), new NoOpProjectStartFailureInjector()).StartAsync(
            new StartProjectCommand(1, actorUserId, Guid.NewGuid(), projectName));
        await using var connection = new SqlConnection(ConnectionString);
        var chatSessionId = await connection.QuerySingleAsync<long>("""
            INSERT dbo.ProjectChatSessions(TenantId, ProjectId, Title)
            OUTPUT inserted.Id
            VALUES (1, @ProjectId, N'Workbench shaping');
            """, new { start.ProjectId });
        var runService = new WorkbenchAgentRunService(
            ConnectionFactory(),
            ServiceProvider.GetRequiredService<IChatTurnPersistenceService>());
        var submitted = await runService.SubmitAsync(new SubmitWorkbenchAgentRunCommand(
            1,
            actorUserId,
            start.ProjectId,
            start.WorkbenchSessionId,
            start.LeaseEpoch,
            Guid.NewGuid(),
            chatSessionId,
            "Shape the acceptance criteria for this product idea."));
        return await runService.ClaimAsync(
                   submitted.AgentRunId,
                   "ba-preparation-audit-test",
                   TimeSpan.FromMinutes(5))
               ?? throw new AssertFailedException("The submitted Workbench agent run was not claimable.");
    }

    private async Task<int> SeedActorAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync("""
            SET IDENTITY_INSERT dbo.Tenants ON;
            INSERT dbo.Tenants(Id, Name, Slug)
            VALUES (1, N'BA Preparation Audit Test', N'ba-preparation-audit-test');
            SET IDENTITY_INSERT dbo.Tenants OFF;
            """);
        var actorUserId = await connection.ExecuteScalarAsync<int>("""
            INSERT dbo.Users(Email, DisplayName, IsActive)
            OUTPUT inserted.Id
            VALUES (N'ba-preparation-audit@irondev.local', N'BA Preparation Auditor', 1);
            """);
        await connection.ExecuteAsync(
            "INSERT dbo.TenantUsers(TenantId, UserId, Role) VALUES (1, @ActorUserId, N'Owner');",
            new { ActorUserId = actorUserId });
        return actorUserId;
    }

    private static WorkbenchBusinessAnalystPreparationProvenance CreateProvenance(
        WorkbenchAgentRunClaim claim)
    {
        var preparedAtUtc = DateTimeOffset.UtcNow;
        var toolCalls = SnapshotToolNames.Select((toolName, index) =>
            new WorkbenchBusinessAnalystToolCallAudit
            {
                ToolName = toolName,
                DefinitionVersion = WorkbenchBusinessAnalystContract.ToolPolicyVersion,
                PolicyVersion = WorkbenchBusinessAnalystContract.ToolPolicyVersion,
                Status = WorkbenchBusinessAnalystToolCallAuditStatus.Completed,
                InputHash = Hash($"input-{index}"),
                OutputHash = Hash($"output-{index}"),
                SafeSummary = $"Recorded bounded snapshot tool outcome {index + 1}.",
                StartedAtUtc = preparedAtUtc.AddMilliseconds(-20 - index),
                CompletedAtUtc = preparedAtUtc.AddMilliseconds(-10 - index)
            }).ToArray();
        return new WorkbenchBusinessAnalystPreparationProvenance
        {
            AgentRunId = claim.AgentRunId,
            ClaimToken = claim.ClaimToken,
            AttemptNumber = claim.AttemptCount,
            EffectiveAnalystProfileHash = "sha256:" + Hash("effective-analyst-profile"),
            AnalystProfilePublishedVersion = 7,
            ActualProvider = "openai",
            ActualModel = "gpt-5.2",
            ProviderTimeoutSeconds = 45,
            PromptHash = Hash("composed-code-owned-prompt"),
            ToolManifestHash = WorkbenchBusinessAnalystPreparationAuditCanonicalizer.ComputeToolManifestHash(
                new WorkbenchBusinessAnalystExecutableContractRegistry().List().Single(contract =>
                    contract.Key.AgentVersion == claim.AgentVersion &&
                    contract.Key.PromptVersion == claim.PromptVersion &&
                    contract.Key.ToolPolicyVersion == claim.ToolPolicyVersion &&
                    contract.Key.ContextSchemaVersion == claim.ContextSchemaVersion &&
                    contract.Key.ContextCanonicalizationVersion == claim.ContextCanonicalizationVersion &&
                    contract.Key.OutputSchemaVersion == claim.OutputSchemaVersion)),
            PreparedAtUtc = preparedAtUtc,
            ToolCalls = toolCalls
        };
    }

    private IDbConnectionFactory ConnectionFactory() =>
        ServiceProvider.GetRequiredService<IDbConnectionFactory>();

    private SqlWorkbenchBusinessAnalystPreparationAuditStore CreateStore() =>
        new(ConnectionFactory(), new WorkbenchBusinessAnalystExecutableContractRegistry());

    private async Task ApplyMigrationAsync(string fileName)
    {
        var sql = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "Database", fileName));
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        foreach (var batch in Regex.Split(
                     sql.Replace("\r\n", "\n", StringComparison.Ordinal),
                     @"(?im)^\s*GO\s*$"))
        {
            if (!string.IsNullOrWhiteSpace(batch))
                await connection.ExecuteAsync(batch);
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed class AuditState
    {
        public int Preparations { get; init; }
        public int ToolCalls { get; init; }
        public string ActualProvider { get; init; } = string.Empty;
        public string ActualModel { get; init; } = string.Empty;
        public int ProviderTimeoutSeconds { get; init; }
    }

    private sealed class AuditCounts
    {
        public int Preparations { get; init; }
        public int ToolCalls { get; init; }
    }
}
