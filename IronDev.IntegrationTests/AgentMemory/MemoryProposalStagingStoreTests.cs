using System.Data;
using Dapper;
using IronDev.Core.AgentMemory;
using IronDev.Data;
using IronDev.Infrastructure.AgentMemory;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.AgentMemory;

[TestClass]
[TestCategory("MemoryProposalStagingStore")]
[TestCategory("RealDatabaseMemoryProposalStagingSmoke")]
public sealed class MemoryProposalStagingStoreTests : IntegrationTestBase
{
    private static readonly Guid ProjectId = Guid.Parse("10000000-dddd-4444-8888-990000000107");
    private static readonly Guid WorkflowRunId = Guid.Parse("20000000-dddd-4444-8888-990000000107");
    private static readonly Guid WorkflowRunStepId = Guid.Parse("30000000-dddd-4444-8888-990000000107");
    private static readonly Guid WorkflowCheckpointId = Guid.Parse("40000000-dddd-4444-8888-990000000107");
    private static readonly Guid GroundingReferenceId = Guid.Parse("50000000-dddd-4444-8888-990000000107");
    private static readonly Guid CorrelationId = Guid.Parse("60000000-dddd-4444-8888-990000000107");

    private SqlMemoryProposalStagingStore _store = default!;

    [TestInitialize]
    public override async Task TestInitialize()
    {
        await base.TestInitialize();
        await DropMemoryProposalSchemaAsync();
        await ApplySqlFileAsync("Database", "migrate_memory_proposal_staging.sql");
        _store = new SqlMemoryProposalStagingStore(ServiceProvider.GetRequiredService<IDbConnectionFactory>());
    }

    [TestCleanup]
    public override async Task TestCleanup()
    {
        await DropMemoryProposalSchemaAsync();
        await base.TestCleanup();
    }

    [TestMethod]
    public void MemoryProposalStagingStore_ExposesCreateReadListOnly()
    {
        var methods = typeof(IMemoryProposalStagingStore).GetMethods().Select(method => method.Name).OrderBy(name => name).ToArray();
        CollectionAssert.AreEquivalent(
            new[] { "CreateAsync", "GetAsync", "ListByProjectAsync", "ListBySourceAsync", "ListByStatusAsync", "ListByWorkflowRunAsync" },
            methods);

        AssertNoForbiddenTokens(
            string.Join("\n", methods),
            "Accept",
            "Approve",
            "Promote",
            "Index",
            "Retrieve",
            "Execute",
            "Continue",
            "ApplySource",
            "MutateSource",
            "CreateCollectiveMemory");
    }

    [TestMethod]
    public async Task MemoryProposalMigration_AddsTablesProceduresTriggersAndRuntimeRoleBoundary()
    {
        Assert.AreEqual(1, await ScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(N'memory.MemoryProposal', N'U') IS NULL THEN 0 ELSE 1 END"));
        Assert.AreEqual(1, await ScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(N'memory.MemoryProposalEvidenceReference', N'U') IS NULL THEN 0 ELSE 1 END"));
        Assert.AreEqual(1, await ScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(N'memory.MemoryProposalGroundingReference', N'U') IS NULL THEN 0 ELSE 1 END"));
        Assert.AreEqual(1, await ScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(N'memory.MemoryProposalWorkflowReference', N'U') IS NULL THEN 0 ELSE 1 END"));
        Assert.AreEqual(1, await ScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(N'memory.usp_MemoryProposal_Create', N'P') IS NULL THEN 0 ELSE 1 END"));
        Assert.AreEqual(1, await ScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(N'memory.usp_MemoryProposal_Get', N'P') IS NULL THEN 0 ELSE 1 END"));
        Assert.AreEqual(1, await ScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(N'memory.usp_MemoryProposal_ListByProject', N'P') IS NULL THEN 0 ELSE 1 END"));
        Assert.AreEqual(1, await ScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(N'memory.usp_MemoryProposal_ListByStatus', N'P') IS NULL THEN 0 ELSE 1 END"));
        Assert.AreEqual(1, await ScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(N'memory.usp_MemoryProposal_ListByWorkflowRun', N'P') IS NULL THEN 0 ELSE 1 END"));
        Assert.AreEqual(1, await ScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(N'memory.usp_MemoryProposal_ListBySource', N'P') IS NULL THEN 0 ELSE 1 END"));
        Assert.AreEqual(1, await ScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(N'memory.TR_MemoryProposal_BlockUpdateDelete', N'TR') IS NULL THEN 0 ELSE 1 END"));
        Assert.AreEqual(1, await ScalarAsync<int>("SELECT CASE WHEN DATABASE_PRINCIPAL_ID(N'IronDevMemoryRuntimeRole') IS NULL THEN 0 ELSE 1 END"));
    }

    [TestMethod]
    public async Task MemoryProposalStagingStore_CreateReadAndList_PreservesEvidenceOnlyProposal()
    {
        var proposal = await _store.CreateAsync(CreateRequest("proposal.real-run.failure-mode"));

        Assert.AreEqual(ProjectId, proposal.ProjectId);
        Assert.AreEqual(MemoryProposalType.FailureModeCandidate, proposal.ProposalType);
        Assert.AreEqual(MemoryProposalTargetScope.ProjectLocalCandidate, proposal.TargetMemoryScope);
        Assert.AreEqual(MemoryProposalStatus.Staged, proposal.ProposalStatus);
        Assert.AreEqual("ManualRealRunMemoryImprovement", proposal.SourceType);
        Assert.AreEqual("run-107", proposal.SourceId);
        Assert.AreEqual(WorkflowRunId, proposal.WorkflowRunId);
        Assert.AreEqual(CorrelationId, proposal.CorrelationId);
        Assert.AreEqual(1, proposal.EvidenceReferences.Count);
        Assert.AreEqual(1, proposal.GroundingReferences.Count);
        Assert.AreEqual(1, proposal.WorkflowReferences.Count);
        AssertProposalHasNoAuthority(proposal);

        var loaded = await _store.GetAsync(ProjectId, proposal.MemoryProposalId);
        Assert.IsNotNull(loaded);
        Assert.AreEqual(proposal.MemoryProposalId, loaded.MemoryProposalId);
        Assert.AreEqual("Repeated validator failure should be reviewed as a possible project-local memory.", loaded.SafeProposedMemory);

        var byProject = await _store.ListByProjectAsync(ProjectId, 10);
        var byStatus = await _store.ListByStatusAsync(ProjectId, MemoryProposalStatus.Staged, 10);
        var byRun = await _store.ListByWorkflowRunAsync(ProjectId, WorkflowRunId, 10);
        var bySource = await _store.ListBySourceAsync(ProjectId, "ManualRealRunMemoryImprovement", "run-107", 10);

        Assert.AreEqual(1, byProject.Count);
        Assert.AreEqual(1, byStatus.Count);
        Assert.AreEqual(1, byRun.Count);
        Assert.AreEqual(1, bySource.Count);
        Assert.AreEqual(1, byProject[0].EvidenceReferenceCount);
        Assert.AreEqual(1, byProject[0].GroundingReferenceCount);
        Assert.AreEqual(1, byProject[0].WorkflowReferenceCount);
    }

    [TestMethod]
    public async Task MemoryProposalStagingStore_RejectsAuthorityFlagsBeforeSql()
    {
        var request = CreateRequest("proposal.bad.authority", promotesMemory: true);
        await ExpectThrowsAsync<InvalidOperationException>(() => _store.CreateAsync(request));
    }

    [TestMethod]
    public async Task MemoryProposalStagingStore_RejectsRawPrivateReasoningBeforeSql()
    {
        var request = CreateRequest("proposal.bad.raw", safeMemory: "rawPrompt leaked into staged memory");
        await ExpectThrowsAsync<InvalidOperationException>(() => _store.CreateAsync(request));
    }

    [TestMethod]
    public async Task MemoryProposalStoredProcedure_RejectsDirectAuthorityAndPrivateReasoningBypass()
    {
        await ExpectThrowsAsync<SqlException>(() => ExecuteCreateProcedureAsync("proposal.sql.raw", safeMemory: "rawPrompt leaked into SQL path"));
        await ExpectThrowsAsync<SqlException>(() => ExecuteCreateProcedureAsync("proposal.sql.promote", promotesMemory: true));
        await ExpectThrowsAsync<SqlException>(() => ExecuteCreateProcedureAsync("proposal.sql.evidence", evidenceJson: "[{\"evidenceType\":\"HumanNote\",\"evidenceId\":\"note-1\",\"safeSummary\":\"rawToolOutput leaked\"}]"));
        await ExpectThrowsAsync<SqlException>(() => ExecuteCreateProcedureAsync("proposal.sql.grounding", groundingJson: "[{\"groundingReferenceId\":\"50000000-dddd-4444-8888-990000000107\",\"claimType\":\"EvidenceSupport\",\"claimId\":\"claim-1\",\"safeSummary\":\"entirePatch leaked\"}]"));
    }

    [TestMethod]
    public async Task MemoryProposalTables_BlockDirectUpdateAndDelete()
    {
        var proposal = await _store.CreateAsync(CreateRequest("proposal.append.only"));

        await ExpectThrowsAsync<SqlException>(() => ExecuteAsync("UPDATE memory.MemoryProposal SET ProposalStatus = N'ReadyForReview' WHERE MemoryProposalId = @MemoryProposalId", new { proposal.MemoryProposalId }));
        await ExpectThrowsAsync<SqlException>(() => ExecuteAsync("DELETE FROM memory.MemoryProposal WHERE MemoryProposalId = @MemoryProposalId", new { proposal.MemoryProposalId }));
    }

    [TestMethod]
    public void MemoryProposalStatuses_DoNotExposeAcceptedPromotedOrIndexedStates()
    {
        var statusNames = Enum.GetNames<MemoryProposalStatus>();
        AssertNoForbiddenTokens(string.Join("\n", statusNames), "Accepted", "Promoted", "Indexed", "Retrieved", "Approved", "Executed");
    }

    [TestMethod]
    public void MemoryProposalProductionFiles_DoNotIntroduceRuntimePromotionRetrievalOrApiWiring()
    {
        var files = new[]
        {
            "IronDev.Core/AgentMemory/MemoryProposalStagingModels.cs",
            "IronDev.Infrastructure/AgentMemory/SqlMemoryProposalStagingStore.cs",
            "Database/migrate_memory_proposal_staging.sql"
        };

        foreach (var file in files)
        {
            var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", file));
            AssertNoForbiddenTokens(
                text,
                "ControllerBase",
                "MapPost",
                "BackgroundService",
                "IHostedService",
                "WorkflowRunner",
                "LangGraph",
                "Weaviate",
                "Embedding",
                "AcceptMemory",
                "PromoteMemoryAsync",
                "CreateCollectiveMemory",
                "ApplySource",
                "MutateSource");
        }
    }

    private static MemoryProposalCreateRequest CreateRequest(
        string proposalKey,
        string safeMemory = "Repeated validator failure should be reviewed as a possible project-local memory.",
        bool promotesMemory = false) => new()
    {
        ProjectId = ProjectId,
        ProposalKey = proposalKey,
        ProposalType = MemoryProposalType.FailureModeCandidate,
        TargetMemoryScope = MemoryProposalTargetScope.ProjectLocalCandidate,
        ProposalStatus = MemoryProposalStatus.Staged,
        SourceType = "ManualRealRunMemoryImprovement",
        SourceId = "run-107",
        SourceAgentRole = "MemoryImprovementAgent",
        SourceAgentId = "memory-improvement-agent",
        SubjectType = "TestFailurePattern",
        SubjectId = "validator-repeat",
        SafeProposedMemory = safeMemory,
        SafeRationaleSummary = "Three governed runs produced the same validation failure shape.",
        SafeRiskSummary = "Proposal requires human review before any memory is accepted or promoted.",
        ConfidenceLabel = "medium",
        ConfidentialityLabel = MemoryProposalConfidentialityLabel.ProjectConfidential,
        SanitizationStatus = MemoryProposalSanitizationStatus.RequiresReview,
        WorkflowRunId = WorkflowRunId,
        WorkflowRunStepId = WorkflowRunStepId,
        WorkflowCheckpointId = WorkflowCheckpointId,
        CorrelationId = CorrelationId,
        CreatedByActorType = "system_test_fixture",
        CreatedByActorId = "memory-proposal-staging-tests",
        MetadataVersion = 1,
        MetadataJson = "{\"source\":\"pr107-test\"}",
        PromotesMemory = promotesMemory,
        EvidenceReferences = new[]
        {
            new MemoryProposalEvidenceReferenceCreateRequest
            {
                EvidenceType = MemoryProposalEvidenceType.WorkflowRun,
                EvidenceId = WorkflowRunId.ToString(),
                EvidenceLabel = "Workflow run evidence",
                SafeSummary = "Run evidence supports review of the staged memory proposal.",
                AllowedUse = MemoryProposalEvidenceAllowedUse.MemoryProposalReview,
                WorkflowRunStepId = WorkflowRunStepId,
                WorkflowCheckpointId = WorkflowCheckpointId
            }
        },
        GroundingReferences = new[]
        {
            new MemoryProposalGroundingReferenceCreateRequest
            {
                GroundingReferenceId = GroundingReferenceId,
                ClaimType = MemoryProposalGroundingClaimType.MemoryProposalTrace,
                ClaimId = "claim-107",
                SafeSummary = "Grounding reference links the staged proposal to review evidence."
            }
        },
        WorkflowReferences = new[]
        {
            new MemoryProposalWorkflowReferenceCreateRequest
            {
                WorkflowRunId = WorkflowRunId,
                WorkflowRunStepId = WorkflowRunStepId,
                WorkflowCheckpointId = WorkflowCheckpointId,
                ReferenceType = MemoryProposalWorkflowReferenceType.GeneratedFrom,
                SafeSummary = "Workflow facts generated the staged proposal candidate."
            }
        }
    };

    private async Task ExecuteCreateProcedureAsync(
        string proposalKey,
        string safeMemory = "SQL path staged proposal remains review-only.",
        bool promotesMemory = false,
        string evidenceJson = "[]",
        string groundingJson = "[]")
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        var parameters = new DynamicParameters();
        parameters.Add("MemoryProposalId", Guid.NewGuid());
        parameters.Add("TenantId", null);
        parameters.Add("ProjectId", ProjectId);
        parameters.Add("ProposalKey", proposalKey);
        parameters.Add("ProposalType", "FailureModeCandidate");
        parameters.Add("TargetMemoryScope", "ProjectLocalCandidate");
        parameters.Add("ProposalStatus", "Staged");
        parameters.Add("SourceType", "SqlHostileTest");
        parameters.Add("SourceId", "sql-107");
        parameters.Add("SafeProposedMemory", safeMemory);
        parameters.Add("ConfidentialityLabel", "ProjectConfidential");
        parameters.Add("SanitizationStatus", "RequiresReview");
        parameters.Add("CreatedByActorType", "system_test_fixture");
        parameters.Add("CreatedByActorId", "memory-proposal-staging-tests");
        parameters.Add("MetadataVersion", 1);
        parameters.Add("MetadataJson", "{}");
        parameters.Add("PromotesMemory", promotesMemory);
        parameters.Add("EvidenceReferencesJson", evidenceJson);
        parameters.Add("GroundingReferencesJson", groundingJson);
        parameters.Add("WorkflowReferencesJson", "[]");

        await connection.QueryMultipleAsync(new CommandDefinition("memory.usp_MemoryProposal_Create", parameters, commandType: CommandType.StoredProcedure));
    }

    private async Task<T> ScalarAsync<T>(string sql, object? parameters = null)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<T>(sql, parameters);
    }

    private async Task ExecuteAsync(string sql, object? parameters = null)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(sql, parameters);
    }

    private async Task ApplySqlFileAsync(params string[] pathSegments)
    {
        var path = Path.Combine(new[] { AppContext.BaseDirectory, "..", "..", "..", ".." }.Concat(pathSegments).ToArray());
        var sql = await File.ReadAllTextAsync(path);
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        foreach (var batch in SplitSqlBatches(sql))
        {
            if (!string.IsNullOrWhiteSpace(batch))
            {
                await connection.ExecuteAsync(batch);
            }
        }
    }

    private async Task DropMemoryProposalSchemaAsync()
    {
        await ExecuteAsync(@"
IF SCHEMA_ID(N'memory') IS NOT NULL
BEGIN
    DROP PROCEDURE IF EXISTS memory.usp_MemoryProposal_ListBySource;
    DROP PROCEDURE IF EXISTS memory.usp_MemoryProposal_ListByWorkflowRun;
    DROP PROCEDURE IF EXISTS memory.usp_MemoryProposal_ListByStatus;
    DROP PROCEDURE IF EXISTS memory.usp_MemoryProposal_ListByProject;
    DROP PROCEDURE IF EXISTS memory.usp_MemoryProposal_Create;
    DROP PROCEDURE IF EXISTS memory.usp_MemoryProposal_Get;
    DROP TRIGGER IF EXISTS memory.TR_MemoryProposalWorkflowReference_BlockUpdateDelete;
    DROP TRIGGER IF EXISTS memory.TR_MemoryProposalWorkflowReference_ValidateInsert;
    DROP TRIGGER IF EXISTS memory.TR_MemoryProposalGroundingReference_BlockUpdateDelete;
    DROP TRIGGER IF EXISTS memory.TR_MemoryProposalGroundingReference_ValidateInsert;
    DROP TRIGGER IF EXISTS memory.TR_MemoryProposalEvidenceReference_BlockUpdateDelete;
    DROP TRIGGER IF EXISTS memory.TR_MemoryProposalEvidenceReference_ValidateInsert;
    DROP TRIGGER IF EXISTS memory.TR_MemoryProposal_BlockUpdateDelete;
    DROP TRIGGER IF EXISTS memory.TR_MemoryProposal_ValidateInsert;
    DROP TABLE IF EXISTS memory.MemoryProposalWorkflowReference;
    DROP TABLE IF EXISTS memory.MemoryProposalGroundingReference;
    DROP TABLE IF EXISTS memory.MemoryProposalEvidenceReference;
    DROP TABLE IF EXISTS memory.MemoryProposal;
    IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE schema_id = SCHEMA_ID(N'memory'))
        EXEC(N'DROP SCHEMA memory');
END");
    }

    private static async Task ExpectThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException)
        {
            return;
        }

        Assert.Fail($"Expected {typeof(TException).Name}.");
    }

    private static IEnumerable<string> SplitSqlBatches(string sql)
    {
        var lines = sql.Replace("\r\n", "\n").Split('\n');
        var current = new List<string>();
        foreach (var line in lines)
        {
            if (line.Trim().Equals("GO", StringComparison.OrdinalIgnoreCase))
            {
                yield return string.Join(Environment.NewLine, current);
                current.Clear();
                continue;
            }

            current.Add(line);
        }

        if (current.Count > 0)
        {
            yield return string.Join(Environment.NewLine, current);
        }
    }

    private static void AssertProposalHasNoAuthority(MemoryProposal proposal)
    {
        Assert.IsFalse(proposal.IsAcceptedMemory);
        Assert.IsFalse(proposal.CreatesAcceptedMemory);
        Assert.IsFalse(proposal.PromotesMemory);
        Assert.IsFalse(proposal.WritesCollectiveMemory);
        Assert.IsFalse(proposal.WritesAgentMemory);
        Assert.IsFalse(proposal.WritesVectorIndex);
        Assert.IsFalse(proposal.IsRetrievalAuthority);
        Assert.IsFalse(proposal.IsPolicy);
        Assert.IsFalse(proposal.IsApproval);
        Assert.IsFalse(proposal.SatisfiesPolicy);
        Assert.IsFalse(proposal.GrantsApproval);
        Assert.IsFalse(proposal.GrantsExecution);
        Assert.IsFalse(proposal.StartsWorkflow);
        Assert.IsFalse(proposal.ContinuesWorkflow);
        Assert.IsFalse(proposal.MutatesSource);
        Assert.IsFalse(proposal.ApprovesRelease);
    }

    private static void AssertNoForbiddenTokens(string text, params string[] forbiddenTokens)
    {
        foreach (var token in forbiddenTokens)
        {
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Forbidden token '{token}' was present.");
        }
    }
}
