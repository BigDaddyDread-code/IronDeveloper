using Dapper;
using IronDev.Core.AgentMemory;
using IronDev.Core.AgentMemory.Execution;
using IronDev.Core.Agents;
using IronDev.Core.Agents.ApprovalPolicy;
using IronDev.Core.Agents.Skills;
using IronDev.Core.Agents.WorkspaceApply;
using IronDev.Data;
using IronDev.Infrastructure.AgentMemory;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace IronDev.IntegrationTests.AgentMemory;

[TestClass]
public sealed class MemoryExecutionAuditTests : IntegrationTestBase
{
    private static readonly DateTimeOffset Now = new(2026, 6, 10, 0, 0, 0, TimeSpan.Zero);
    private SqlMemoryExecutionAuditStore _auditStore = null!;

    [TestInitialize]
    public override async Task TestInitialize()
    {
        await base.TestInitialize();
        await DropAgentMemorySchemaAsync();
        await ApplyAgentMemoryMigrationsAsync();
        _auditStore = new SqlMemoryExecutionAuditStore(ServiceProvider.GetRequiredService<IDbConnectionFactory>());
    }

    [TestCleanup]
    public override async Task TestCleanup()
    {
        try
        {
            await DropAgentMemorySchemaAsync();
        }
        catch
        {
            // Cleanup should not hide the real assertion failure.
        }

        await base.TestCleanup();
    }

    [TestMethod]
    public async Task MemoryExecutionAudit_SchemaBlocksUpdateAndDelete()
    {
        var record = await _auditStore.AppendAsync(BuildDraft());

        await ExpectSqlFailsAsync($"UPDATE agent.AgentMemoryExecutionAudit SET Summary = 'changed' WHERE AuditId = '{record.AuditId}';");
        await ExpectSqlFailsAsync($"DELETE FROM agent.AgentMemoryExecutionAudit WHERE AuditId = '{record.AuditId}';");
    }

    [TestMethod]
    public async Task MemoryExecutionAudit_QueryFiltersByDecisionExecutionGovernanceAndReferences()
    {
        var record = await _auditStore.AppendAsync(BuildDraft());

        AssertSingle(await _auditStore.QueryAsync(BuildQuery() with { DecisionId = record.DecisionId }), record.AuditId);
        AssertSingle(await _auditStore.QueryAsync(BuildQuery() with { ExecutionId = record.ExecutionId }), record.AuditId);
        AssertSingle(await _auditStore.QueryAsync(BuildQuery() with { GovernanceCheckId = record.GovernanceCheckId }), record.AuditId);
        AssertSingle(await _auditStore.QueryAsync(BuildQuery() with { MemoryItemId = "memory-1" }), record.AuditId);
        AssertSingle(await _auditStore.QueryAsync(BuildQuery() with { InfluenceId = "influence-1" }), record.AuditId);
        AssertSingle(await _auditStore.QueryAsync(BuildQuery() with { HandoffMemorySliceId = "handoff-1" }), record.AuditId);

        var wrongRun = await _auditStore.QueryAsync(BuildQuery() with { RunId = "run-other", MemoryItemId = "memory-1" });
        Assert.AreEqual(0, wrongRun.Count);
    }

    [TestMethod]
    public async Task MemoryExecutionAudit_QueryTakeClampsToFiveHundred()
    {
        for (var index = 0; index < 3; index++)
        {
            await _auditStore.AppendAsync(BuildDraft(
                executionId: $"execution-{index}",
                decisionId: $"decision-{index}",
                memoryId: $"memory-{index}"));
        }

        var records = await _auditStore.QueryAsync(BuildQuery() with { Take = 9999 });

        Assert.AreEqual(3, records.Count);
    }

    [TestMethod]
    public async Task MemoryExecutionAudit_RejectsMissingDecisionId()
    {
        var draft = BuildDraft(decisionId: "");

        await ExpectInvalidOperationAsync(() => _auditStore.AppendAsync(draft));
    }

    [TestMethod]
    public async Task MemoryExecutionAudit_RejectsNoMemoryInfluenceOrHandoffReferences()
    {
        var draft = BuildDraft(memoryId: "", influenceId: "", handoffId: "");

        await ExpectInvalidOperationAsync(() => _auditStore.AppendAsync(draft));
    }

    [TestMethod]
    public async Task MemoryExecutionAudit_RejectsNonMemoryResult()
    {
        var draft = BuildDraft() with
        {
            Result = BuildResult(includeMemoryEvidence: false)
        };

        await ExpectInvalidOperationAsync(() => _auditStore.AppendAsync(draft));
    }

    [TestMethod]
    public async Task MemoryExecutionAudit_RejectsMismatchedEvidenceDecisionId()
    {
        var draft = BuildDraft() with
        {
            Result = BuildResult(memoryEvidence: BuildEvidence(decisionId: "other-decision"))
        };

        await ExpectInvalidOperationAsync(() => _auditStore.AppendAsync(draft));
    }

    [TestMethod]
    public async Task MemoryExecutionAudit_RejectsRawPrivateReasoningMarkers()
    {
        var draft = BuildDraft() with
        {
            Result = BuildResult(summary: "RawPrompt should never be persisted.")
        };

        await ExpectInvalidOperationAsync(() => _auditStore.AppendAsync(draft));
    }

    [TestMethod]
    public async Task MemoryExecutionAudit_NonMemorySkillExecution_DoesNotAppendAudit()
    {
        var audit = new AgentSkillExecutionTestMemoryExecutionAuditStore();
        var service = AgentSkillExecutionTestServices.Create(
            workspaceApplyContextService: new AuditTestApplyContextService(),
            memoryExecutionAuditStore: audit);

        var result = await service.ExecuteAsync(BuildExecutionRequest(includeMemoryContext: false));

        Assert.AreEqual(AgentSkillExecutionStatuses.Succeeded, result.Status);
        Assert.AreEqual(0, audit.Records.Count);
    }

    [TestMethod]
    public async Task MemoryExecutionAudit_MemoryBackedSuccess_WritesAudit()
    {
        var audit = new AgentSkillExecutionTestMemoryExecutionAuditStore();
        var service = AgentSkillExecutionTestServices.Create(
            workspaceApplyContextService: new AuditTestApplyContextService(),
            memoryExecutionGate: new AuditTestMemoryGate(BuildGateResult(MemoryExecutionGateDecision.Allowed)),
            memoryExecutionAuditStore: audit);

        var result = await service.ExecuteAsync(BuildExecutionRequest());

        Assert.AreEqual(AgentSkillExecutionStatuses.Succeeded, result.Status);
        Assert.AreEqual(1, audit.Records.Count);
        Assert.AreEqual(MemoryExecutionAuditOutcome.ExecutedSucceeded, audit.Records.Single().Outcome);
        Assert.AreEqual(result.ExecutionId, audit.Records.Single().ExecutionId);
        CollectionAssert.Contains(audit.Records.Single().MemoryItemIds.ToArray(), "memory-1");
    }

    [TestMethod]
    public async Task MemoryExecutionAudit_MemoryGateBlock_WritesAudit()
    {
        var audit = new AgentSkillExecutionTestMemoryExecutionAuditStore();
        var service = AgentSkillExecutionTestServices.Create(
            workspaceApplyContextService: new AuditTestApplyContextService(),
            memoryExecutionGate: new AuditTestMemoryGate(BuildGateResult(MemoryExecutionGateDecision.Blocked, mayProceed: false)),
            memoryExecutionAuditStore: audit);

        var result = await service.ExecuteAsync(BuildExecutionRequest());

        Assert.AreEqual(AgentSkillExecutionStatuses.BlockedByMemory, result.Status);
        Assert.AreEqual(1, audit.Records.Count);
        Assert.AreEqual(MemoryExecutionAuditOutcome.BlockedByMemory, audit.Records.Single().Outcome);
    }

    [TestMethod]
    public async Task MemoryExecutionAudit_PolicyBlock_WritesAudit()
    {
        var audit = new AgentSkillExecutionTestMemoryExecutionAuditStore();
        var service = AgentSkillExecutionTestServices.Create(
            workspaceApplyContextService: new AuditTestApplyContextService(),
            memoryExecutionGate: new AuditTestMemoryGate(BuildGateResult(MemoryExecutionGateDecision.Allowed)),
            memoryExecutionAuditStore: audit);

        var result = await service.ExecuteAsync(BuildExecutionRequest(BuildContext() with
        {
            Decision = ProjectApprovalDecisions.BlockedByPolicy,
            PolicyAllowed = false,
            PolicyBlocked = true
        }));

        Assert.AreEqual(AgentSkillExecutionStatuses.BlockedByPolicy, result.Status);
        Assert.AreEqual(MemoryExecutionAuditOutcome.BlockedByPolicy, audit.Records.Single().Outcome);
    }

    [TestMethod]
    public async Task MemoryExecutionAudit_ApprovalBlock_WritesAudit()
    {
        var audit = new AgentSkillExecutionTestMemoryExecutionAuditStore();
        var service = AgentSkillExecutionTestServices.Create(
            workspaceApplyContextService: new AuditTestApplyContextService(),
            memoryExecutionGate: new AuditTestMemoryGate(BuildGateResult(MemoryExecutionGateDecision.Allowed)),
            memoryExecutionAuditStore: audit);

        var result = await service.ExecuteAsync(BuildExecutionRequest(BuildContext() with
        {
            HumanApprovalRequired = true,
            ReviewStatus = AgentSkillRequestReviewStatuses.ApprovalRequired,
            RecommendedNextAction = AgentSkillRequestContextRecommendedActions.RequestSeparateApproval
        }));

        Assert.AreEqual(AgentSkillExecutionStatuses.BlockedByContext, result.Status);
        Assert.AreEqual(MemoryExecutionAuditOutcome.BlockedByApproval, audit.Records.Single().Outcome);
    }

    [TestMethod]
    public async Task MemoryExecutionAudit_InvalidContext_WritesAudit()
    {
        var audit = new AgentSkillExecutionTestMemoryExecutionAuditStore();
        var service = AgentSkillExecutionTestServices.Create(
            workspaceApplyContextService: new AuditTestApplyContextService(),
            memoryExecutionGate: new AuditTestMemoryGate(BuildGateResult(MemoryExecutionGateDecision.Allowed)),
            memoryExecutionAuditStore: audit);

        var result = await service.ExecuteAsync(BuildExecutionRequest(BuildContext() with
        {
            RecommendedNextAction = AgentSkillRequestContextRecommendedActions.CollectMissingEvidence
        }));

        Assert.AreEqual(AgentSkillExecutionStatuses.BlockedByContext, result.Status);
        Assert.AreEqual(MemoryExecutionAuditOutcome.BlockedByContext, audit.Records.Single().Outcome);
    }

    [TestMethod]
    public async Task MemoryExecutionAudit_FailedExecution_WritesAudit()
    {
        var audit = new AgentSkillExecutionTestMemoryExecutionAuditStore();
        var service = AgentSkillExecutionTestServices.Create(
            workspaceApplyContextService: null,
            memoryExecutionGate: new AuditTestMemoryGate(BuildGateResult(MemoryExecutionGateDecision.Allowed)),
            memoryExecutionAuditStore: audit);

        var result = await service.ExecuteAsync(BuildExecutionRequest());

        Assert.AreEqual(AgentSkillExecutionStatuses.Failed, result.Status);
        Assert.AreEqual(MemoryExecutionAuditOutcome.ExecutedBlockedByTool, audit.Records.Single().Outcome);
    }

    [TestMethod]
    public async Task MemoryExecutionAudit_AuditWriteFailure_FailsMemoryBackedExecution()
    {
        var service = AgentSkillExecutionTestServices.Create(
            workspaceApplyContextService: new AuditTestApplyContextService(),
            memoryExecutionGate: new AuditTestMemoryGate(BuildGateResult(MemoryExecutionGateDecision.Allowed)),
            memoryExecutionAuditStore: new ThrowingMemoryExecutionAuditStore());

        var result = await service.ExecuteAsync(BuildExecutionRequest());

        Assert.AreEqual(AgentSkillExecutionStatuses.Failed, result.Status);
        Assert.IsTrue(result.Blockers.Any(item => item.Contains("audit", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task MemoryExecutionAudit_AppendDoesNotMutateSourceMemoryOrCreateAuthority()
    {
        var connectionFactory = ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        var memoryStore = new SqlAgentLocalMemoryStore(connectionFactory, new AgentMemoryContractValidator());
        await memoryStore.CreateAsync(BuildMemoryItem("memory-1"));
        var eventCountBefore = await CountMemoryEventsAsync("memory-1");

        await _auditStore.AppendAsync(BuildDraft(memoryId: "memory-1"));

        var eventCountAfter = await CountMemoryEventsAsync("memory-1");
        var acceptedOrSystem = await CountAcceptedOrSystemRuleMemoryAsync();
        Assert.AreEqual(eventCountBefore, eventCountAfter);
        Assert.AreEqual(0, acceptedOrSystem);
    }

    private static MemoryExecutionAuditQuery BuildQuery() =>
        new()
        {
            TenantId = "tenant-1",
            ProjectId = "project-1",
            CampaignId = "campaign-1",
            RunId = "run-1",
            AgentId = "critic-agent"
        };

    private static MemoryExecutionAuditDraft BuildDraft(
        string executionId = "execution-1",
        string decisionId = "decision-1",
        string memoryId = "memory-1",
        string influenceId = "influence-1",
        string handoffId = "handoff-1") =>
        new()
        {
            Request = BuildExecutionRequest(memoryContext: BuildMemoryContext(decisionId, memoryId, influenceId, handoffId)),
            Result = BuildResult(executionId, memoryEvidence: BuildEvidence(decisionId, memoryId, influenceId, handoffId)),
            GateResult = BuildGateResult(MemoryExecutionGateDecision.Allowed, decisionId: decisionId, memoryId: memoryId, influenceId: influenceId, handoffId: handoffId),
            Outcome = MemoryExecutionAuditOutcome.ExecutedSucceeded,
            CreatedAt = Now
        };

    private static AgentSkillExecutionRequest BuildExecutionRequest(
        AgentSkillRequestContext? context = null,
        MemoryBackedExecutionContext? memoryContext = null,
        bool includeMemoryContext = true) =>
        new()
        {
            SkillRequestContext = context ?? BuildContext(),
            RequestedByAgent = "CriticAgent",
            ProjectId = "IronDev",
            RunId = "run-1",
            WorkspacePath = "C:\\workspaces\\run-1",
            SourceRepo = "C:\\repo\\IronDeveloper",
            MemoryExecutionContext = includeMemoryContext ? memoryContext ?? BuildMemoryContext() : null,
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["runId"] = "run-1",
                ["workspacePath"] = "C:\\workspaces\\run-1"
            }
        };

    private static AgentSkillRequestContext BuildContext() =>
        new()
        {
            ContextId = "skill-context-memory-audit",
            RequestId = "skill-request-memory-audit",
            ReviewId = "skill-review-memory-audit",
            ProjectId = "IronDev",
            AgentName = "CriticAgent",
            SkillId = AgentSkillIds.WorkspaceReadApplyContext,
            Purpose = "Read governed workspace apply context.",
            SkillKnown = true,
            Decision = ProjectApprovalDecisions.AllowedByPolicy,
            ReviewStatus = AgentSkillRequestReviewStatuses.ReadyForHumanReview,
            RiskTier = ProjectApprovalRiskTiers.WorkspaceReporting,
            Category = AgentSkillCategories.WorkspaceContext,
            HumanReviewRequired = true,
            HumanApprovalRequired = false,
            PolicyAllowed = true,
            PolicyBlocked = false,
            DangerousCapability = false,
            ExecutionCanStartFromContext = false,
            ApprovalCanBeGrantedByContext = false,
            SourceMutationAllowed = false,
            WorkspaceMutationAllowed = false,
            ExternalSystemAllowed = false,
            CreatesTicketAllowed = false,
            WritesMemoryAllowed = false,
            RecommendedNextAction = AgentSkillRequestContextRecommendedActions.ReviewRequest,
            EvidencePaths = ["context.json"],
            ParametersSummary = ["runId=run-1", "workspacePath=C:\\workspaces\\run-1"],
            ReviewChecklist = ["Confirm this context is not execution authority."],
            Blockers = [],
            Warnings = [],
            Interpretation = ["Context is reviewable but not execution authority."]
        };

    private static MemoryBackedExecutionContext BuildMemoryContext(
        string decisionId = "decision-1",
        string memoryId = "memory-1",
        string influenceId = "influence-1",
        string handoffId = "handoff-1") =>
        new()
        {
            Scope = BuildScope(),
            ActionType = MemoryGovernanceActionType.ContextUse,
            DecisionId = decisionId,
            ReferencedArtifacts =
            [
                new MemoryBackedExecutionReference
                {
                    MemoryItemId = string.IsNullOrWhiteSpace(memoryId) ? null : memoryId,
                    InfluenceId = string.IsNullOrWhiteSpace(influenceId) ? null : influenceId,
                    HandoffMemorySliceId = string.IsNullOrWhiteSpace(handoffId) ? null : handoffId,
                    DecisionId = decisionId,
                    ThoughtLedgerEntryId = "thought-1"
                }
            ],
            RequestedAt = Now,
            ToolName = "workspace.read_apply_context",
            CorrelationId = "correlation-1",
            InfluenceRecordRequired = false
        };

    private static AgentMemoryScope BuildScope() =>
        new()
        {
            TenantId = "tenant-1",
            ProjectId = "project-1",
            CampaignId = "campaign-1",
            RunId = "run-1",
            AgentId = "critic-agent"
        };

    private static AgentSkillExecutionResult BuildResult(
        string executionId = "execution-1",
        string summary = "Read-only skill execution completed.",
        MemoryExecutionEvidence? memoryEvidence = null,
        bool includeMemoryEvidence = true) =>
        new()
        {
            ExecutionId = executionId,
            ContextId = "skill-context-memory-audit",
            RequestId = "skill-request-memory-audit",
            ReviewId = "skill-review-memory-audit",
            SkillId = AgentSkillIds.WorkspaceReadApplyContext,
            Status = AgentSkillExecutionStatuses.Succeeded,
            Summary = summary,
            Executed = true,
            ReadOnlyExecution = true,
            SourceMutated = false,
            WorkspaceMutated = false,
            ExternalSystemCalled = false,
            TicketCreated = false,
            MemoryWritten = false,
            ApprovalGranted = false,
            ShellCommandRun = false,
            Payload = null,
            MemoryEvidence = includeMemoryEvidence ? memoryEvidence ?? BuildEvidence() : null,
            EvidencePaths = ["source-report.json"],
            Warnings = [],
            Blockers = []
        };

    private static MemoryExecutionEvidence BuildEvidence(
        string decisionId = "decision-1",
        string memoryId = "memory-1",
        string influenceId = "influence-1",
        string handoffId = "handoff-1") =>
        new()
        {
            IsMemoryBacked = true,
            GovernanceCheckId = "governance-check-1",
            DecisionId = decisionId,
            GateDecision = MemoryExecutionGateDecision.Allowed,
            GovernanceDecision = MemoryGovernanceDecision.Allow,
            IssueCodes = [],
            MemoryItemIds = string.IsNullOrWhiteSpace(memoryId) ? [] : [memoryId],
            InfluenceIds = string.IsNullOrWhiteSpace(influenceId) ? [] : [influenceId],
            HandoffMemorySliceIds = string.IsNullOrWhiteSpace(handoffId) ? [] : [handoffId]
        };

    private static MemoryExecutionGateResult BuildGateResult(
        MemoryExecutionGateDecision decision,
        bool mayProceed = true,
        string decisionId = "decision-1",
        string memoryId = "memory-1",
        string influenceId = "influence-1",
        string handoffId = "handoff-1") =>
        new()
        {
            Decision = decision,
            MayProceedToPolicyGate = mayProceed,
            Summary = $"Synthetic memory gate result: {decision}.",
            GovernanceResult = new MemoryGovernanceCheckResult
            {
                GovernanceCheckId = "governance-check-1",
                Scope = BuildScope(),
                DecisionId = decisionId,
                ActionType = MemoryGovernanceActionType.ContextUse,
                Decision = decision == MemoryExecutionGateDecision.Blocked
                    ? MemoryGovernanceDecision.Block
                    : MemoryGovernanceDecision.Allow,
                Issues = [],
                CheckedAt = Now,
                CorrelationId = "correlation-1",
                ThoughtLedgerEntryId = "thought-1"
            },
            Issues = [],
            Evidence = BuildEvidence(decisionId, memoryId, influenceId, handoffId) with
            {
                GateDecision = decision,
                GovernanceDecision = decision == MemoryExecutionGateDecision.Blocked
                    ? MemoryGovernanceDecision.Block
                    : MemoryGovernanceDecision.Allow
            }
        };

    private static AgentLocalMemoryItem BuildMemoryItem(string memoryItemId) =>
        new()
        {
            MemoryItemId = memoryItemId,
            Scope = BuildScope(),
            MemoryType = AgentMemoryType.Episodic,
            AuthorityLevel = MemoryAuthorityLevel.ObservedOnly,
            Status = MemoryLifecycleStatus.Active,
            Title = "Observed build failure",
            Summary = "TesterAgent observed a reproducible build failure.",
            EvidenceRefs =
            [
                new EvidenceRef
                {
                    EvidenceId = "evidence-memory-1",
                    EvidenceType = EvidenceType.TestResult,
                    SourceId = "source-evidence-memory-1"
                }
            ],
            Confidence = 0.8m,
            CreatedAt = Now
        };

    private async Task<int> CountMemoryEventsAsync(string memoryItemId)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        return await connection.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM agent.AgentLocalMemoryEvent WHERE MemoryItemId = @MemoryItemId;",
            new { MemoryItemId = memoryItemId });
    }

    private async Task<int> CountAcceptedOrSystemRuleMemoryAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        return await connection.QuerySingleAsync<int>(
            """
            SELECT COUNT(*)
            FROM agent.AgentLocalMemoryItem
            WHERE AuthorityLevel IN (@Accepted, @SystemRule);
            """,
            new
            {
                Accepted = (int)MemoryAuthorityLevel.Accepted,
                SystemRule = (int)MemoryAuthorityLevel.SystemRule
            });
    }

    private static void AssertSingle(IReadOnlyList<MemoryExecutionAuditRecord> records, string auditId)
    {
        Assert.AreEqual(1, records.Count);
        Assert.AreEqual(auditId, records.Single().AuditId);
    }

    private async Task ExpectSqlFailsAsync(string sql)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        try
        {
            await connection.ExecuteAsync(sql);
        }
        catch (SqlException)
        {
            return;
        }

        Assert.Fail($"Expected SQL mutation to fail but it succeeded: {sql}");
    }


    private static async Task ExpectInvalidOperationAsync(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            return;
        }

        Assert.Fail("Expected InvalidOperationException was not thrown.");
    }
    private async Task ApplyAgentMemoryMigrationsAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(), "Database", "migrate_agent_local_memory.sql")));
        await connection.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(), "Database", "migrate_agent_memory_influence.sql")));
        await connection.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(), "Database", "migrate_agent_memory_handoff.sql")));
        await connection.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(), "Database", "migrate_agent_memory_execution_audit.sql")));
    }

    private async Task DropAgentMemorySchemaAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            """
            IF OBJECT_ID('agent.TR_AgentMemoryExecutionAudit_ValidateInsert', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentMemoryExecutionAudit_ValidateInsert;
            IF OBJECT_ID('agent.TR_AgentMemoryExecutionAudit_BlockUpdateDelete', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentMemoryExecutionAudit_BlockUpdateDelete;
            IF OBJECT_ID('agent.AgentMemoryExecutionAudit', 'U') IS NOT NULL
                DROP TABLE agent.AgentMemoryExecutionAudit;
            IF OBJECT_ID('agent.TR_AgentMemoryHandoffSlice_ValidateSourceMemory', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentMemoryHandoffSlice_ValidateSourceMemory;
            IF OBJECT_ID('agent.TR_AgentMemoryHandoffSlice_BlockUpdateDelete', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentMemoryHandoffSlice_BlockUpdateDelete;
            IF OBJECT_ID('agent.AgentMemoryHandoffSlice', 'U') IS NOT NULL
                DROP TABLE agent.AgentMemoryHandoffSlice;
            IF OBJECT_ID('agent.TR_AgentMemoryInfluenceRecord_ValidateScope', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentMemoryInfluenceRecord_ValidateScope;
            IF OBJECT_ID('agent.TR_AgentMemoryInfluenceRecord_BlockUpdateDelete', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentMemoryInfluenceRecord_BlockUpdateDelete;
            IF OBJECT_ID('agent.AgentMemoryInfluenceRecord', 'U') IS NOT NULL
                DROP TABLE agent.AgentMemoryInfluenceRecord;
            IF OBJECT_ID('agent.vwAgentLocalMemoryCurrentState', 'V') IS NOT NULL
                DROP VIEW agent.vwAgentLocalMemoryCurrentState;
            IF OBJECT_ID('agent.TR_AgentLocalMemoryEvent_ValidateInsert', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentLocalMemoryEvent_ValidateInsert;
            IF OBJECT_ID('agent.TR_AgentLocalMemoryEvidenceRef_BlockUpdateDelete', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentLocalMemoryEvidenceRef_BlockUpdateDelete;
            IF OBJECT_ID('agent.TR_AgentLocalMemoryEvent_BlockUpdateDelete', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentLocalMemoryEvent_BlockUpdateDelete;
            IF OBJECT_ID('agent.TR_AgentLocalMemoryItem_BlockUpdateDelete', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentLocalMemoryItem_BlockUpdateDelete;
            IF OBJECT_ID('agent.AgentLocalMemoryEvidenceRef', 'U') IS NOT NULL
                DROP TABLE agent.AgentLocalMemoryEvidenceRef;
            IF OBJECT_ID('agent.AgentLocalMemoryEvent', 'U') IS NOT NULL
                DROP TABLE agent.AgentLocalMemoryEvent;
            IF OBJECT_ID('agent.AgentLocalMemoryItem', 'U') IS NOT NULL
                DROP TABLE agent.AgentLocalMemoryItem;
            IF SCHEMA_ID('agent') IS NOT NULL
                DROP SCHEMA agent;
            """);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root for memory execution audit tests.");
    }

    private sealed class AuditTestApplyContextService : IAgentWorkspaceApplyContextService
    {
        public Task<AgentWorkspaceApplyContext> CreateAsync(
            AgentWorkspaceApplyContextRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AgentWorkspaceApplyContext
            {
                ProjectId = request.ProjectId,
                RunId = request.RunId,
                WorkspacePath = request.WorkspacePath,
                ContextAvailable = true,
                WorkspaceApply = new WorkspaceApplyReportSummary
                {
                    RunId = request.RunId,
                    WorkspacePath = request.WorkspacePath,
                    SourceRepo = "C:\\repo\\IronDeveloper",
                    Outcome = "success",
                    Recommendation = "ready_for_human_review",
                    SourceRepoMutated = true,
                    ApplyVerified = true,
                    SourceMatchesWorkspace = true,
                    PostApplyValidationSucceeded = true,
                    EvidencePaths = ["source-report.json"],
                    RiskNotes = ["Human should review changed files before commit."]
                },
                EvidencePaths = ["source-report.json"],
                Warnings = []
            });
    }

    private sealed class AuditTestMemoryGate : IMemoryExecutionGate
    {
        private readonly MemoryExecutionGateResult _result;

        public AuditTestMemoryGate(MemoryExecutionGateResult result)
        {
            _result = result;
        }

        public Task<MemoryExecutionGateResult> EvaluateAsync(
            MemoryBackedExecutionContext? context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_result);
    }

    private sealed class ThrowingMemoryExecutionAuditStore : IMemoryExecutionAuditStore
    {
        public Task<MemoryExecutionAuditRecord> AppendAsync(
            MemoryExecutionAuditDraft draft,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Synthetic audit write failure.");

        public Task<IReadOnlyList<MemoryExecutionAuditRecord>> QueryAsync(
            MemoryExecutionAuditQuery query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MemoryExecutionAuditRecord>>(Array.Empty<MemoryExecutionAuditRecord>());
    }
}
