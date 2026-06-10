using System.Reflection;
using Dapper;
using IronDev.Core.AgentMemory;
using IronDev.Data;
using IronDev.Infrastructure.AgentMemory;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class AgentMemoryRunReportTests : IntegrationTestBase
{
    private static readonly DateTimeOffset Now = new(2026, 6, 10, 0, 0, 0, TimeSpan.Zero);

    private AgentMemorySiloService _siloService = null!;
    private SqlAgentMemoryRunReportService _reportService = null!;

    [TestInitialize]
    public override async Task TestInitialize()
    {
        await base.TestInitialize();
        await DropAgentMemorySchemaAsync();
        await ApplyAgentMemoryMigrationsAsync();

        var connectionFactory = ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        var store = new SqlAgentLocalMemoryStore(connectionFactory, new AgentMemoryContractValidator());
        var influenceStore = new SqlAgentMemoryInfluenceStore(connectionFactory);
        var handoffStore = new SqlAgentMemoryHandoffStore(connectionFactory);

        _siloService = new AgentMemorySiloService(store, influenceStore, handoffStore);
        _reportService = new SqlAgentMemoryRunReportService(connectionFactory);
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
            // Test cleanup should not hide the original assertion failure.
        }

        await base.TestCleanup();
    }

    [TestMethod]
    public async Task AgentMemoryRunReport_IncludesPerAgentMemoryInfluenceAndHandoffs()
    {
        await SeedRunMemoryAsync();

        var report = await _reportService.BuildAsync(BuildRequest());

        Assert.AreEqual("tenant-1", report.TenantId);
        Assert.AreEqual("project-1", report.ProjectId);
        Assert.AreEqual("campaign-1", report.CampaignId);
        Assert.AreEqual("run-1", report.RunId);
        Assert.AreEqual(2, report.AgentCount);
        Assert.AreEqual(3, report.TotalMemoryItemsCreated);
        Assert.AreEqual(2, report.TotalInfluenceRecords);
        Assert.AreEqual(2, report.TotalHandoffSlices);

        var builder = report.Agents.Single(item => item.AgentId == "builder-agent");
        Assert.AreEqual(3, builder.CreatedMemoryCount);
        Assert.AreEqual(2, builder.LifecycleEventCount);
        Assert.AreEqual(2, builder.InfluenceRecordCount);
        Assert.AreEqual(2, builder.OutgoingHandoffCount);
        Assert.AreEqual(0, builder.IncomingHandoffCount);

        var tester = report.Agents.Single(item => item.AgentId == "tester-agent");
        Assert.AreEqual(0, tester.CreatedMemoryCount);
        Assert.AreEqual(0, tester.InfluenceRecordCount);
        Assert.AreEqual(0, tester.OutgoingHandoffCount);
        Assert.AreEqual(2, tester.IncomingHandoffCount);

        Assert.IsTrue(builder.ActivityReferences.Any(item => item.Kind == MemoryActivityKind.LocalMemoryCreated));
        Assert.IsTrue(builder.ActivityReferences.Any(item => item.Kind == MemoryActivityKind.LocalMemoryLifecycleEvent));
        Assert.IsTrue(builder.ActivityReferences.Any(item => item.Kind == MemoryActivityKind.MemoryInfluenceRecorded));
        Assert.IsTrue(builder.ActivityReferences.Any(item => item.Kind == MemoryActivityKind.MemoryHandoffOutgoing));
        Assert.IsTrue(tester.ActivityReferences.All(item => item.Kind == MemoryActivityKind.MemoryHandoffIncoming));
    }

    [TestMethod]
    public async Task AgentMemoryRunReport_PreservesThoughtLedgerReferencesWithoutCreatingLedgerStore()
    {
        await SeedRunMemoryAsync();

        var report = await _reportService.BuildAsync(BuildRequest());
        var builder = report.Agents.Single(item => item.AgentId == "builder-agent");

        var proposed = builder.MemoryItems.Single(item => item.MemoryItemId == "memory-proposed");
        Assert.IsTrue(proposed.LifecycleThoughtLedgerEntryIds.Contains("thought-event-proposed"));

        var influence = builder.InfluenceRecords.Single(item => item.InfluenceId == "influence-expired");
        Assert.AreEqual("thought-influence-expired", influence.ThoughtLedgerEntryId);

        var handoff = builder.OutgoingHandoffs.Single(item => item.HandoffMemorySliceId == "handoff-traced");
        Assert.AreEqual("thought-handoff-1", handoff.ThoughtLedgerEntryId);
    }

    [TestMethod]
    public async Task AgentMemoryRunReport_GeneratesDeterministicReviewFindings()
    {
        await SeedRunMemoryAsync();

        var report = await _reportService.BuildAsync(BuildRequest());

        AssertHasFinding(report, RunMemoryFindingType.LowConfidenceInfluence, influenceId: "influence-low");
        AssertHasFinding(report, RunMemoryFindingType.NeedsVerificationHandoff, handoffId: "handoff-needs");
        AssertHasFinding(report, RunMemoryFindingType.ProposedMemoryHandedOff, memoryItemId: "memory-proposed");
        AssertHasFinding(report, RunMemoryFindingType.CandidatePatternMemory, memoryItemId: "memory-candidate");
        AssertHasFinding(report, RunMemoryFindingType.ExpiredMemoryHadInfluence, influenceId: "influence-expired");
        AssertHasFinding(report, RunMemoryFindingType.MissingThoughtLedgerReference, influenceId: "influence-low");
        AssertHasFinding(report, RunMemoryFindingType.MissingThoughtLedgerReference, handoffId: "handoff-needs");

        var builder = report.Agents.Single(item => item.AgentId == "builder-agent");
        Assert.IsTrue(builder.ReviewCandidates.Any(item => item.FindingType == RunMemoryFindingType.CandidatePatternMemory));

        var tester = report.Agents.Single(item => item.AgentId == "tester-agent");
        Assert.IsTrue(tester.ReviewCandidates.Any(item => item.FindingType == RunMemoryFindingType.NeedsVerificationHandoff));
    }

    [TestMethod]
    public async Task AgentMemoryRunReport_EnforcesRunScopeAcrossAgents()
    {
        await SeedRunMemoryAsync();

        var foreign = _siloService.Open(BuildContext(tenantId: "tenant-foreign", agentId: "foreign-agent"));
        await foreign.CreateAsync(BuildMemoryDraft("memory-foreign"));

        var report = await _reportService.BuildAsync(BuildRequest());

        Assert.IsFalse(report.Agents.Any(item => item.AgentId == "foreign-agent"));
        Assert.IsFalse(report.Agents.SelectMany(item => item.MemoryItems).Any(item => item.MemoryItemId == "memory-foreign"));
    }

    [TestMethod]
    public async Task AgentMemoryRunReport_ClampsTakePerAgent()
    {
        var report = await _reportService.BuildAsync(BuildRequest() with { TakePerAgent = 1_000 });

        Assert.AreEqual(500, report.TakePerAgent);
    }

    [TestMethod]
    public void AgentMemoryRunReport_IsNotExposedThroughAgentMemorySilo()
    {
        var siloMethods = typeof(IAgentMemorySilo).GetMethods(BindingFlags.Instance | BindingFlags.Public);

        Assert.IsFalse(siloMethods.Any(method =>
            method.Name.Contains("RunReport", StringComparison.OrdinalIgnoreCase) ||
            method.ReturnType == typeof(RunMemoryReport)));
    }

    [TestMethod]
    public void AgentMemoryRunReport_ModelsDoNotExposeRawReasoningPayloads()
    {
        var reportTypes = new[]
        {
            typeof(MemoryActivityReference),
            typeof(MemoryThoughtLedgerEntryDraft),
            typeof(RunMemoryReportRequest),
            typeof(RunMemoryReport),
            typeof(AgentRunMemoryReport),
            typeof(AgentMemoryReportItem),
            typeof(AgentMemoryInfluenceReportItem),
            typeof(AgentMemoryHandoffReportItem),
            typeof(AgentMemoryReviewCandidate),
            typeof(RunMemoryFinding)
        };

        var propertyNames = reportTypes
            .SelectMany(type => type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            .Select(property => property.Name)
            .ToArray();

        string[] banned =
        [
            "RawPrompt",
            "Prompt",
            "RawCompletion",
            "Completion",
            "ChainOfThought",
            "Scratchpad",
            "PrivateReasoning"
        ];

        foreach (var bannedName in banned)
        {
            Assert.IsFalse(
                propertyNames.Any(name => name.Contains(bannedName, StringComparison.OrdinalIgnoreCase)),
                $"Run memory report models must not expose raw private reasoning field '{bannedName}'.");
        }
    }

    private async Task SeedRunMemoryAsync()
    {
        var builder = _siloService.Open(BuildContext(agentId: "builder-agent"));

        await builder.CreateAsync(BuildCandidatePatternDraft("memory-candidate"));
        await builder.RecordInfluenceAsync(BuildInfluenceDraft("influence-low", "memory-candidate") with
        {
            Confidence = 0.4m,
            ThoughtLedgerEntryId = null
        });

        await builder.CreateAsync(BuildMemoryDraft("memory-proposed"));
        await builder.AddEventAsync(BuildEventDraft(
            "memory-proposed",
            AgentLocalMemoryEventType.ProposedForReview,
            1,
            "thought-event-proposed"));

        await builder.CreateAsync(BuildMemoryDraft("memory-expired"));
        await builder.RecordInfluenceAsync(BuildInfluenceDraft("influence-expired", "memory-expired") with
        {
            ThoughtLedgerEntryId = "thought-influence-expired"
        });
        await builder.AddEventAsync(BuildEventDraft(
            "memory-expired",
            AgentLocalMemoryEventType.Expired,
            2,
            "thought-event-expired"));

        await builder.CreateHandoffAsync(BuildHandoffDraft("handoff-needs", "tester-agent", ["memory-proposed"]) with
        {
            AllowedUse = HandoffMemoryAllowedUse.NeedsVerification,
            ThoughtLedgerEntryId = null
        });

        await builder.CreateHandoffAsync(BuildHandoffDraft("handoff-traced", "tester-agent", ["memory-candidate"]) with
        {
            ThoughtLedgerEntryId = "thought-handoff-1"
        });
    }

    private async Task ApplyAgentMemoryMigrationsAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(), "Database", "migrate_agent_local_memory.sql")));
        await connection.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(), "Database", "migrate_agent_memory_influence.sql")));
        await connection.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(), "Database", "migrate_agent_memory_handoff.sql")));
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
                DROP TABLE agent.AgentMemoryExecutionAudit;            IF OBJECT_ID('agent.TR_AgentMemoryHandoffSlice_ValidateSourceMemory', 'TR') IS NOT NULL
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

        throw new InvalidOperationException("Could not locate repository root for agent memory run report tests.");
    }

    private static RunMemoryReportRequest BuildRequest() =>
        new()
        {
            TenantId = "tenant-1",
            ProjectId = "project-1",
            CampaignId = "campaign-1",
            RunId = "run-1"
        };

    private static AgentMemorySiloContext BuildContext(
        string tenantId = "tenant-1",
        string projectId = "project-1",
        string campaignId = "campaign-1",
        string runId = "run-1",
        string agentId = "builder-agent") =>
        new()
        {
            TenantId = tenantId,
            ProjectId = projectId,
            CampaignId = campaignId,
            RunId = runId,
            AgentId = agentId,
            WorkflowId = "workflow-1",
            TicketId = "ticket-1",
            CorrelationId = "correlation-1"
        };

    private static AgentLocalMemoryDraft BuildMemoryDraft(string memoryItemId) =>
        new()
        {
            MemoryItemId = memoryItemId,
            MemoryType = AgentMemoryType.Episodic,
            AuthorityLevel = MemoryAuthorityLevel.ObservedOnly,
            Title = "Observed build failure",
            Summary = "TesterAgent observed a reproducible build failure.",
            EvidenceRefs = [BuildEvidence()],
            Confidence = 0.8m,
            CreatedAt = Now
        };

    private static AgentLocalMemoryDraft BuildCandidatePatternDraft(string memoryItemId) =>
        BuildMemoryDraft(memoryItemId) with
        {
            MemoryType = AgentMemoryType.CandidatePattern,
            AuthorityLevel = MemoryAuthorityLevel.CandidatePattern,
            Title = "Candidate retry pattern",
            Summary = "Repeated validation failures may share a cause.",
            KnownLimitations = "Observed during one run only and must not be promoted without review."
        };

    private static AgentLocalMemoryEventDraft BuildEventDraft(
        string memoryItemId,
        AgentLocalMemoryEventType eventType,
        int minutesAfterCreated,
        string thoughtLedgerEntryId) =>
        new()
        {
            MemoryEventId = $"event-{memoryItemId}-{eventType}-{minutesAfterCreated}",
            MemoryItemId = memoryItemId,
            EventType = eventType,
            EventReason = $"Lifecycle event {eventType}.",
            CreatedAt = Now.AddMinutes(minutesAfterCreated),
            CreatedByUserId = "human-reviewer",
            DecisionId = "decision-event",
            ThoughtLedgerEntryId = thoughtLedgerEntryId,
            EventJson = "{}"
        };

    private static MemoryInfluenceDraft BuildInfluenceDraft(string influenceId, string memoryItemId) =>
        new()
        {
            InfluenceId = influenceId,
            MemoryItemId = memoryItemId,
            DecisionId = $"decision-{influenceId}",
            InfluenceType = MemoryInfluenceType.HandoffIncluded,
            InfluenceSummary = "Memory was selected for an explicit handoff.",
            EvidenceRefs = [BuildEvidence($"evidence-{influenceId}")],
            Confidence = 0.7m,
            CreatedAt = Now.AddMinutes(5),
            ThoughtLedgerEntryId = $"thought-{influenceId}"
        };

    private static HandoffMemorySliceDraft BuildHandoffDraft(
        string handoffId,
        string targetAgentId,
        IReadOnlyList<string> memoryItemIds) =>
        new()
        {
            HandoffMemorySliceId = handoffId,
            TargetAgentId = targetAgentId,
            MemoryItemIds = memoryItemIds,
            Summary = "Builder hands bounded memory context to Tester.",
            AllowedUse = HandoffMemoryAllowedUse.ContextOnly,
            EvidenceRefs = [BuildEvidence($"evidence-{handoffId}")],
            Confidence = 0.7m,
            CreatedAt = Now.AddMinutes(10),
            ThoughtLedgerEntryId = $"thought-{handoffId}",
            CorrelationId = "correlation-1",
            HandoffJson = "{}"
        };

    private static EvidenceRef BuildEvidence(string evidenceId = "evidence-1") =>
        new()
        {
            EvidenceId = evidenceId,
            EvidenceType = EvidenceType.TestResult,
            SourceId = $"source-{evidenceId}",
            SourceUri = $"workspace://run-1/{evidenceId}.json",
            Summary = "Focused test result captured during the run.",
            CapturedAt = Now
        };

    private static void AssertHasFinding(
        RunMemoryReport report,
        RunMemoryFindingType findingType,
        string? memoryItemId = null,
        string? influenceId = null,
        string? handoffId = null)
    {
        Assert.IsTrue(
            report.Findings.Any(finding =>
                finding.FindingType == findingType &&
                (memoryItemId is null || finding.MemoryItemId == memoryItemId) &&
                (influenceId is null || finding.InfluenceId == influenceId) &&
                (handoffId is null || finding.HandoffMemorySliceId == handoffId)),
            $"Expected finding {findingType}.");
    }
}
