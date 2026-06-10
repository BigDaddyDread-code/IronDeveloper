using System.Reflection;
using System.Text;
using System.Text.Json;
using Dapper;
using IronDev.Core.AgentMemory;
using IronDev.Core.AgentMemory.Evaluation;
using IronDev.Core.AgentMemory.Execution;
using IronDev.Core.Agents.ApprovalPolicy;
using IronDev.Core.Agents.Skills;
using IronDev.Data;
using IronDev.Infrastructure.AgentMemory;
using Microsoft.Data.SqlClient;

namespace IronDev.IntegrationTests;

public sealed class MemoryGovernanceEvaluationHarness : IMemoryGovernanceEvaluationHarness
{
    private static readonly DateTimeOffset Now = new(2026, 6, 10, 0, 0, 0, TimeSpan.Zero);
    private static readonly string[] BannedPrivateReasoningTokens =
    [
        "RawPrompt",
        "Prompt",
        "RawCompletion",
        "Completion",
        "ChainOfThought",
        "Scratchpad",
        "PrivateReasoning"
    ];

    private readonly string _connectionString;
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly string _repositoryRoot;

    public MemoryGovernanceEvaluationHarness(
        string connectionString,
        IDbConnectionFactory connectionFactory,
        string repositoryRoot)
    {
        _connectionString = connectionString;
        _connectionFactory = connectionFactory;
        _repositoryRoot = repositoryRoot;
    }

    public async Task<MemoryEvaluationRunResult> RunAsync(CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var scenarios = new (MemoryEvaluationScenarioId Id, string Name, Func<HarnessServices, CancellationToken, Task<IReadOnlyList<string>>> Run)[]
        {
            (MemoryEvaluationScenarioId.CrossAgentLocalMemoryReadBlocked, nameof(MemoryEvaluationScenarioId.CrossAgentLocalMemoryReadBlocked), RunCrossAgentLocalMemoryReadBlockedAsync),
            (MemoryEvaluationScenarioId.CrossRunLocalMemoryReadBlocked, nameof(MemoryEvaluationScenarioId.CrossRunLocalMemoryReadBlocked), RunCrossRunLocalMemoryReadBlockedAsync),
            (MemoryEvaluationScenarioId.TerminalMemoryCannotInfluenceAction, nameof(MemoryEvaluationScenarioId.TerminalMemoryCannotInfluenceAction), RunTerminalMemoryCannotInfluenceActionAsync),
            (MemoryEvaluationScenarioId.InfluenceOnlyExpiredMemoryBlocked, nameof(MemoryEvaluationScenarioId.InfluenceOnlyExpiredMemoryBlocked), RunInfluenceOnlyExpiredMemoryBlockedAsync),
            (MemoryEvaluationScenarioId.HandoffDoesNotGrantSourceMemoryAccess, nameof(MemoryEvaluationScenarioId.HandoffDoesNotGrantSourceMemoryAccess), RunHandoffDoesNotGrantSourceMemoryAccessAsync),
            (MemoryEvaluationScenarioId.NonTargetHandoffUseBlocked, nameof(MemoryEvaluationScenarioId.NonTargetHandoffUseBlocked), RunNonTargetHandoffUseBlockedAsync),
            (MemoryEvaluationScenarioId.ProposalAcceptedDoesNotPromoteMemory, nameof(MemoryEvaluationScenarioId.ProposalAcceptedDoesNotPromoteMemory), RunProposalAcceptedDoesNotPromoteMemoryAsync),
            (MemoryEvaluationScenarioId.ProposalCannotReferenceForeignMemory, nameof(MemoryEvaluationScenarioId.ProposalCannotReferenceForeignMemory), RunProposalCannotReferenceForeignMemoryAsync),
            (MemoryEvaluationScenarioId.WeaviateDoesNotIndexRawLocalMemory, nameof(MemoryEvaluationScenarioId.WeaviateDoesNotIndexRawLocalMemory), RunWeaviateDoesNotIndexRawLocalMemoryAsync),
            (MemoryEvaluationScenarioId.WeaviateReviewedPositiveDoesNotPromoteMemory, nameof(MemoryEvaluationScenarioId.WeaviateReviewedPositiveDoesNotPromoteMemory), RunWeaviateReviewedPositiveDoesNotPromoteMemoryAsync),
            (MemoryEvaluationScenarioId.SourceMutationNeverAllowedByMemoryAlone, nameof(MemoryEvaluationScenarioId.SourceMutationNeverAllowedByMemoryAlone), RunSourceMutationNeverAllowedByMemoryAloneAsync),
            (MemoryEvaluationScenarioId.ExternalEffectNeverAllowedByMemoryAlone, nameof(MemoryEvaluationScenarioId.ExternalEffectNeverAllowedByMemoryAlone), RunExternalEffectNeverAllowedByMemoryAloneAsync),
            (MemoryEvaluationScenarioId.RawReasoningRejectedEverywhere, nameof(MemoryEvaluationScenarioId.RawReasoningRejectedEverywhere), RunRawReasoningRejectedEverywhereAsync),
            (MemoryEvaluationScenarioId.AppendOnlyMutationBlocked, nameof(MemoryEvaluationScenarioId.AppendOnlyMutationBlocked), RunAppendOnlyMutationBlockedAsync),
            (MemoryEvaluationScenarioId.RunReportDoesNotLeakOtherRun, nameof(MemoryEvaluationScenarioId.RunReportDoesNotLeakOtherRun), RunRunReportDoesNotLeakOtherRunAsync),
            (MemoryEvaluationScenarioId.SiloDoesNotExposeGovernanceOrIndexingServices, nameof(MemoryEvaluationScenarioId.SiloDoesNotExposeGovernanceOrIndexingServices), RunSiloDoesNotExposeGovernanceOrIndexingServicesAsync),
            (MemoryEvaluationScenarioId.MemoryBackedExecutionCannotBypassGate, nameof(MemoryEvaluationScenarioId.MemoryBackedExecutionCannotBypassGate), RunMemoryBackedExecutionCannotBypassGateAsync),
            (MemoryEvaluationScenarioId.MemoryBackedExecutionProducesAuditPackage, nameof(MemoryEvaluationScenarioId.MemoryBackedExecutionProducesAuditPackage), RunMemoryBackedExecutionProducesAuditPackageAsync)
        };

        var results = new List<MemoryEvaluationScenarioResult>();
        foreach (var scenario in scenarios)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await RunScenarioAsync(scenario.Id, scenario.Name, scenario.Run, cancellationToken).ConfigureAwait(false));
        }

        return new MemoryEvaluationRunResult
        {
            EvaluationRunId = $"memory-eval-{startedAt:yyyyMMddHHmmss}",
            StartedAt = startedAt,
            CompletedAt = DateTimeOffset.UtcNow,
            ScenarioCount = results.Count,
            PassedCount = results.Count(item => item.Passed),
            FailedCount = results.Count(item => !item.Passed),
            Scenarios = results
        };
    }

    public static string FormatReport(MemoryEvaluationRunResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Memory Governance Evaluation Run");
        builder.AppendLine($"Scenarios: {result.ScenarioCount}");
        builder.AppendLine($"Passed: {result.PassedCount}");
        builder.AppendLine($"Failed: {result.FailedCount}");
        builder.AppendLine();

        foreach (var scenario in result.Scenarios.OrderBy(item => item.ScenarioId))
        {
            builder.AppendLine($"[{(scenario.Passed ? "PASS" : "FAIL")}] {scenario.ScenarioId}");
            builder.AppendLine($"  Summary: {scenario.Summary}");
            builder.AppendLine("  Evidence:");
            foreach (var evidence in scenario.Evidence)
                builder.AppendLine($"  - {evidence}");

            if (scenario.FailureReasons.Count > 0)
            {
                builder.AppendLine("  Failure reasons:");
                foreach (var reason in scenario.FailureReasons)
                    builder.AppendLine($"  - {reason}");
            }
        }

        return builder.ToString();
    }

    private async Task<MemoryEvaluationScenarioResult> RunScenarioAsync(
        MemoryEvaluationScenarioId scenarioId,
        string name,
        Func<HarnessServices, CancellationToken, Task<IReadOnlyList<string>>> run,
        CancellationToken cancellationToken)
    {
        try
        {
            await ResetSchemaAsync(cancellationToken).ConfigureAwait(false);
            var services = BuildServices();
            var evidence = await run(services, cancellationToken).ConfigureAwait(false);
            Ensure(evidence.Count > 0, "Scenario returned no evidence.");

            return new MemoryEvaluationScenarioResult
            {
                ScenarioId = scenarioId,
                Name = name,
                Passed = true,
                Summary = "Dangerous bypass attempt was blocked or constrained as expected.",
                Evidence = evidence
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new MemoryEvaluationScenarioResult
            {
                ScenarioId = scenarioId,
                Name = name,
                Passed = false,
                Summary = "Scenario failed; the governance boundary did not produce the expected evidence.",
                Evidence = [$"Exception type: {ex.GetType().Name}"],
                FailureReasons = [ex.Message]
            };
        }
    }

    private HarnessServices BuildServices()
    {
        var memoryStore = new SqlAgentLocalMemoryStore(_connectionFactory, new AgentMemoryContractValidator());
        var influenceStore = new SqlAgentMemoryInfluenceStore(_connectionFactory);
        var handoffStore = new SqlAgentMemoryHandoffStore(_connectionFactory);
        var proposalService = new SqlMemoryImprovementProposalService(_connectionFactory);
        var queueStore = new SqlMemoryIndexQueueStore(_connectionFactory);
        var indexer = new FakeWeaviateMemoryIndexer();

        return new HarnessServices(
            new AgentMemorySiloService(memoryStore, influenceStore, handoffStore),
            proposalService,
            new SqlAgentMemoryRunReportService(_connectionFactory),
            new SqlConscienceMemoryGovernanceService(_connectionFactory),
            new SqlMemoryExecutionAuditStore(_connectionFactory),
            queueStore,
            new MemoryIndexingService(new SqlMemoryIndexProjectionBuilder(_connectionFactory), queueStore, indexer),
            indexer);
    }

    private async Task<IReadOnlyList<string>> RunCrossAgentLocalMemoryReadBlockedAsync(HarnessServices services, CancellationToken cancellationToken)
    {
        var builder = OpenSilo(services, "builder-agent");
        var tester = OpenSilo(services, "tester-agent");
        await builder.CreateAsync(BuildMemoryDraft("memory-builder-only"), cancellationToken).ConfigureAwait(false);

        var directGet = await tester.GetAsync("memory-builder-only", cancellationToken).ConfigureAwait(false);
        var query = await tester.QueryAsync(new AgentLocalMemoryQuery(), cancellationToken).ConfigureAwait(false);
        var governance = await services.Governance.CheckAsync(BuildMemoryRequest(
            "memory-builder-only",
            MemoryGovernanceActionType.ContextUse,
            BuildScope(agentId: "tester-agent"),
            influenceRequired: false), cancellationToken).ConfigureAwait(false);

        Ensure(directGet is null, "TesterAgent read BuilderAgent local memory by ID.");
        Ensure(query.Count == 0, "TesterAgent query returned BuilderAgent local memory.");
        EnsureIssue(governance, MemoryGovernanceDecision.Block, MemoryGovernanceIssueCode.MemoryNotFoundInScope);

        return ["Tester Get returned null", "Tester Query returned 0", "Conscience issue MemoryNotFoundInScope observed"];
    }

    private async Task<IReadOnlyList<string>> RunCrossRunLocalMemoryReadBlockedAsync(HarnessServices services, CancellationToken cancellationToken)
    {
        var run1 = OpenSilo(services, "builder-agent", runId: "run-1");
        var run2 = OpenSilo(services, "builder-agent", runId: "run-2");
        await run1.CreateAsync(BuildMemoryDraft("memory-run-1-only"), cancellationToken).ConfigureAwait(false);

        var directGet = await run2.GetAsync("memory-run-1-only", cancellationToken).ConfigureAwait(false);
        var governance = await services.Governance.CheckAsync(BuildMemoryRequest(
            "memory-run-1-only",
            MemoryGovernanceActionType.ContextUse,
            BuildScope(runId: "run-2"),
            influenceRequired: false), cancellationToken).ConfigureAwait(false);

        Ensure(directGet is null, "Run-2 scope read run-1 local memory.");
        EnsureIssue(governance, MemoryGovernanceDecision.Block, MemoryGovernanceIssueCode.MemoryNotFoundInScope);

        return ["Run-2 Get returned null", "Conscience issue MemoryNotFoundInScope observed"];
    }

    private async Task<IReadOnlyList<string>> RunTerminalMemoryCannotInfluenceActionAsync(HarnessServices services, CancellationToken cancellationToken)
    {
        var silo = OpenSilo(services, "builder-agent");
        await silo.CreateAsync(BuildMemoryDraft("memory-expired"), cancellationToken).ConfigureAwait(false);
        await silo.AddEventAsync(BuildEventDraft("memory-expired", AgentLocalMemoryEventType.Expired, 1), cancellationToken).ConfigureAwait(false);
        await silo.CreateAsync(BuildMemoryDraft("memory-invalidated"), cancellationToken).ConfigureAwait(false);
        await silo.AddEventAsync(BuildEventDraft("memory-invalidated", AgentLocalMemoryEventType.Invalidated, 1), cancellationToken).ConfigureAwait(false);
        await silo.CreateAsync(BuildMemoryDraft("memory-superseded"), cancellationToken).ConfigureAwait(false);
        await silo.AddEventAsync(BuildEventDraft("memory-superseded", AgentLocalMemoryEventType.Superseded, 1), cancellationToken).ConfigureAwait(false);

        EnsureIssue(await services.Governance.CheckAsync(BuildMemoryRequest("memory-expired", MemoryGovernanceActionType.ToolCallJustification, influenceRequired: false), cancellationToken).ConfigureAwait(false), MemoryGovernanceDecision.Block, MemoryGovernanceIssueCode.MemoryExpired);
        EnsureIssue(await services.Governance.CheckAsync(BuildMemoryRequest("memory-invalidated", MemoryGovernanceActionType.ToolCallJustification, influenceRequired: false), cancellationToken).ConfigureAwait(false), MemoryGovernanceDecision.Block, MemoryGovernanceIssueCode.MemoryInvalidated);
        EnsureIssue(await services.Governance.CheckAsync(BuildMemoryRequest("memory-superseded", MemoryGovernanceActionType.ToolCallJustification, influenceRequired: false), cancellationToken).ConfigureAwait(false), MemoryGovernanceDecision.Block, MemoryGovernanceIssueCode.MemorySuperseded);

        return ["Expired memory blocked", "Invalidated memory blocked", "Superseded memory blocked"];
    }

    private async Task<IReadOnlyList<string>> RunInfluenceOnlyExpiredMemoryBlockedAsync(HarnessServices services, CancellationToken cancellationToken)
    {
        var silo = OpenSilo(services, "builder-agent");
        await silo.CreateAsync(BuildMemoryDraft("memory-influence-expired"), cancellationToken).ConfigureAwait(false);
        await silo.RecordInfluenceAsync(BuildInfluenceDraft("influence-expired", "memory-influence-expired", "decision-1"), cancellationToken).ConfigureAwait(false);
        await silo.AddEventAsync(BuildEventDraft("memory-influence-expired", AgentLocalMemoryEventType.Expired, 1), cancellationToken).ConfigureAwait(false);

        var result = await services.Governance.CheckAsync(BuildInfluenceOnlyRequest("influence-expired", MemoryGovernanceActionType.ToolCallJustification), cancellationToken).ConfigureAwait(false);
        EnsureIssue(result, MemoryGovernanceDecision.Block, MemoryGovernanceIssueCode.MemoryExpired);

        return ["Influence-only request resolved source memory", "Expired source memory produced MemoryExpired block"];
    }

    private async Task<IReadOnlyList<string>> RunHandoffDoesNotGrantSourceMemoryAccessAsync(HarnessServices services, CancellationToken cancellationToken)
    {
        var builder = OpenSilo(services, "builder-agent");
        var tester = OpenSilo(services, "tester-agent");
        await builder.CreateAsync(BuildMemoryDraft("memory-handed-off"), cancellationToken).ConfigureAwait(false);
        await builder.CreateHandoffAsync(BuildHandoffDraft("handoff-to-tester", "tester-agent", ["memory-handed-off"]), cancellationToken).ConfigureAwait(false);

        var incoming = await tester.QueryIncomingHandoffsAsync(new HandoffMemorySliceQuery(), cancellationToken).ConfigureAwait(false);
        var directGet = await tester.GetAsync("memory-handed-off", cancellationToken).ConfigureAwait(false);
        var query = await tester.QueryAsync(new AgentLocalMemoryQuery(), cancellationToken).ConfigureAwait(false);

        Ensure(incoming.Count == 1, "TesterAgent did not receive addressed handoff.");
        Ensure(directGet is null, "Handoff granted direct source memory access.");
        Ensure(query.Count == 0, "TesterAgent local query returned source-agent memory.");

        return ["Incoming handoff exists", "Direct source memory read returned null", "Tester local query returned 0"];
    }

    private async Task<IReadOnlyList<string>> RunNonTargetHandoffUseBlockedAsync(HarnessServices services, CancellationToken cancellationToken)
    {
        var builder = OpenSilo(services, "builder-agent");
        await builder.CreateAsync(BuildMemoryDraft("memory-non-target-handoff"), cancellationToken).ConfigureAwait(false);
        await builder.CreateHandoffAsync(BuildHandoffDraft("handoff-not-for-critic", "tester-agent", ["memory-non-target-handoff"]), cancellationToken).ConfigureAwait(false);

        var result = await services.Governance.CheckAsync(BuildHandoffRequest(
            "handoff-not-for-critic",
            MemoryGovernanceActionType.ContextUse,
            BuildScope(agentId: "critic-agent")), cancellationToken).ConfigureAwait(false);
        EnsureIssue(result, MemoryGovernanceDecision.Block, MemoryGovernanceIssueCode.HandoffNotAddressedToAgent);

        return ["CriticAgent use of TesterAgent handoff blocked", "Conscience issue HandoffNotAddressedToAgent observed"];
    }

    private async Task<IReadOnlyList<string>> RunMemoryBackedExecutionCannotBypassGateAsync(HarnessServices services, CancellationToken cancellationToken)
    {
        var silo = OpenSilo(services, "builder-agent");
        await silo.CreateAsync(BuildMemoryDraft("memory-execution-expired"), cancellationToken).ConfigureAwait(false);
        await silo.RecordInfluenceAsync(BuildInfluenceDraft("influence-execution-expired", "memory-execution-expired", "decision-execution"), cancellationToken).ConfigureAwait(false);
        await silo.AddEventAsync(BuildEventDraft("memory-execution-expired", AgentLocalMemoryEventType.Expired, 1), cancellationToken).ConfigureAwait(false);

        var gate = new MemoryExecutionGate(services.Governance);
        var result = await gate.EvaluateAsync(new MemoryBackedExecutionContext
        {
            Scope = BuildScope(),
            ActionType = MemoryGovernanceActionType.ToolCallJustification,
            DecisionId = "decision-execution",
            ReferencedArtifacts =
            [
                new MemoryBackedExecutionReference
                {
                    MemoryItemId = "memory-execution-expired",
                    InfluenceId = "influence-execution-expired",
                    DecisionId = "decision-execution",
                    ThoughtLedgerEntryId = "thought-execution-gate"
                }
            ],
            RequestedAt = Now.AddMinutes(6),
            ToolName = "workspace.validate",
            CorrelationId = "correlation-1"
        }, cancellationToken).ConfigureAwait(false);

        Ensure(result.Decision == MemoryExecutionGateDecision.Blocked, $"Expected memory execution gate to block, got {result.Decision}.");
        Ensure(!result.MayProceedToPolicyGate, "Memory execution gate allowed policy evaluation after expired memory.");
        Ensure(result.Evidence.IsMemoryBacked, "Memory execution gate did not mark evidence as memory-backed.");
        Ensure(result.Evidence.IssueCodes.Contains(MemoryGovernanceIssueCode.MemoryExpired), "Memory execution gate evidence did not include MemoryExpired.");

        return ["Memory execution gate evaluated real Conscience result", "Expired memory-backed execution blocked before policy", "MemoryExpired issue captured in execution evidence"];
    }

    private async Task<IReadOnlyList<string>> RunMemoryBackedExecutionProducesAuditPackageAsync(HarnessServices services, CancellationToken cancellationToken)
    {
        var silo = OpenSilo(services, "builder-agent");
        await silo.CreateAsync(BuildMemoryDraft("memory-execution-audit"), cancellationToken).ConfigureAwait(false);
        await silo.RecordInfluenceAsync(BuildInfluenceDraft("influence-execution-audit", "memory-execution-audit", "decision-execution-audit"), cancellationToken).ConfigureAwait(false);

        var gate = new MemoryExecutionGate(services.Governance);
        var context = new MemoryBackedExecutionContext
        {
            Scope = BuildScope(),
            ActionType = MemoryGovernanceActionType.ToolCallJustification,
            DecisionId = "decision-execution-audit",
            ReferencedArtifacts =
            [
                new MemoryBackedExecutionReference
                {
                    MemoryItemId = "memory-execution-audit",
                    InfluenceId = "influence-execution-audit",
                    DecisionId = "decision-execution-audit",
                    ThoughtLedgerEntryId = "thought-execution-audit"
                }
            ],
            RequestedAt = Now.AddMinutes(6),
            ToolName = "workspace.validate",
            CorrelationId = "correlation-1"
        };
        var gateResult = await gate.EvaluateAsync(context, cancellationToken).ConfigureAwait(false);
        Ensure(gateResult.MayProceedToPolicyGate, "Memory execution gate did not allow audit scenario to proceed.");

        await services.ExecutionAuditStore.AppendAsync(new MemoryExecutionAuditDraft
        {
            Request = BuildAuditExecutionRequest(context),
            Result = BuildAuditExecutionResult(gateResult.Evidence),
            GateResult = gateResult,
            Outcome = MemoryExecutionAuditOutcome.ExecutedSucceeded,
            CreatedAt = Now.AddMinutes(7)
        }, cancellationToken).ConfigureAwait(false);

        var records = await services.ExecutionAuditStore.QueryAsync(new MemoryExecutionAuditQuery
        {
            TenantId = "tenant-1",
            ProjectId = "project-1",
            CampaignId = "campaign-1",
            RunId = "run-1",
            AgentId = "builder-agent",
            DecisionId = "decision-execution-audit",
            MemoryItemId = "memory-execution-audit",
            InfluenceId = "influence-execution-audit"
        }, cancellationToken).ConfigureAwait(false);

        Ensure(records.Count == 1, $"Expected one memory execution audit record, found {records.Count}.");
        var audit = records.Single();
        Ensure(audit.Outcome == MemoryExecutionAuditOutcome.ExecutedSucceeded, $"Expected audit outcome ExecutedSucceeded, got {audit.Outcome}.");
        Ensure(audit.GovernanceCheckId == gateResult.GovernanceResult!.GovernanceCheckId, "Audit did not preserve governance check ID.");
        Ensure(audit.DecisionId == "decision-execution-audit", "Audit did not preserve execution decision ID.");
        Ensure(audit.MemoryItemIds.Contains("memory-execution-audit"), "Audit did not preserve memory item reference.");
        Ensure(audit.InfluenceIds.Contains("influence-execution-audit"), "Audit did not preserve influence reference.");

        return ["Memory-backed execution wrote durable audit package", "Audit links decision/governance/memory/influence", "Audit outcome captured execution success"];
    }

    private async Task<IReadOnlyList<string>> RunProposalAcceptedDoesNotPromoteMemoryAsync(HarnessServices services, CancellationToken cancellationToken)
    {
        var silo = OpenSilo(services, "builder-agent");
        await silo.CreateAsync(BuildMemoryDraft("memory-proposal-source"), cancellationToken).ConfigureAwait(false);
        var eventCountBefore = await CountMemoryEventsAsync("memory-proposal-source", cancellationToken).ConfigureAwait(false);

        await services.ProposalService.CreateAsync(BuildProposalDraft("proposal-accepted", MemorySource("memory-proposal-source")), cancellationToken).ConfigureAwait(false);
        await services.ProposalService.AddEventAsync(BuildScope(), BuildProposalEvent("proposal-accepted", MemoryImprovementProposalEventType.AcceptedForFutureImplementation), cancellationToken).ConfigureAwait(false);

        Ensure(await GetMemoryAuthorityAsync("memory-proposal-source", cancellationToken).ConfigureAwait(false) == MemoryAuthorityLevel.ObservedOnly, "Accepted proposal promoted source memory authority.");
        Ensure(await GetCurrentMemoryEventTypeAsync("memory-proposal-source", cancellationToken).ConfigureAwait(false) == AgentLocalMemoryEventType.Created, "Accepted proposal changed source memory lifecycle.");
        Ensure(await CountAcceptedOrSystemRuleMemoryAsync(cancellationToken).ConfigureAwait(false) == 0, "Accepted proposal created Accepted/SystemRule memory.");
        Ensure(eventCountBefore == await CountMemoryEventsAsync("memory-proposal-source", cancellationToken).ConfigureAwait(false), "Proposal service added lifecycle event to source memory.");

        return ["Memory authority stayed ObservedOnly", "Memory status stayed Active", "Accepted/SystemRule count = 0", "Source memory event count unchanged"];
    }

    private async Task<IReadOnlyList<string>> RunProposalCannotReferenceForeignMemoryAsync(HarnessServices services, CancellationToken cancellationToken)
    {
        var builder = OpenSilo(services, "builder-agent");
        await builder.CreateAsync(BuildMemoryDraft("memory-foreign-proposal-source"), cancellationToken).ConfigureAwait(false);

        await ExpectExceptionAsync<InvalidOperationException>(() => services.ProposalService.CreateAsync(
            BuildProposalDraft("proposal-foreign-memory", MemorySource("memory-foreign-proposal-source"), BuildScope(agentId: "critic-agent")) with
            {
                ProposedByAgentId = "critic-agent"
            }, cancellationToken)).ConfigureAwait(false);

        await ExpectSqlFailsAsync(
            """
            INSERT INTO agent.AgentMemoryImprovementProposal
            (
                ProposalId,
                TenantId,
                ProjectId,
                CampaignId,
                RunId,
                AgentId,
                ProposalType,
                Title,
                Summary,
                SourcesJson,
                EvidenceRefsJson,
                Confidence,
                ProposedByAgentId,
                CreatedAtUtc
            )
            VALUES
            (
                'proposal-direct-foreign-memory',
                'tenant-1',
                'project-1',
                'campaign-1',
                'run-1',
                'critic-agent',
                2,
                'Direct proposal',
                'Direct SQL tried to reference another agent memory item.',
                '[{"memoryItemId":"memory-foreign-proposal-source"}]',
                '[{"evidenceId":"evidence-direct","evidenceType":10,"sourceId":"run-report"}]',
                0.7000,
                'critic-agent',
                SYSUTCDATETIME()
            );
            """,
            cancellationToken).ConfigureAwait(false);

        return ["Proposal service rejected foreign memory source", "Direct SQL source validation rejected foreign memory source"];
    }

    private async Task<IReadOnlyList<string>> RunWeaviateDoesNotIndexRawLocalMemoryAsync(HarnessServices services, CancellationToken cancellationToken)
    {
        var builder = OpenSilo(services, "builder-agent");
        await builder.CreateAsync(BuildMemoryDraft("memory-index-source"), cancellationToken).ConfigureAwait(false);

        await services.IndexingService.QueueRunAsync("tenant-1", "project-1", "campaign-1", "run-1", cancellationToken).ConfigureAwait(false);
        await services.IndexingService.ProcessPendingAsync("tenant-1", "project-1", 100, cancellationToken).ConfigureAwait(false);

        var allowed = new HashSet<MemoryIndexArtifactType>
        {
            MemoryIndexArtifactType.RunMemoryReport,
            MemoryIndexArtifactType.AgentRunMemoryReport,
            MemoryIndexArtifactType.MemoryImprovementProposal,
            MemoryIndexArtifactType.MemoryImprovementProposalEvent,
            MemoryIndexArtifactType.MemoryInfluenceSummary,
            MemoryIndexArtifactType.HandoffSummary
        };

        Ensure(services.Indexer.Indexed.Count > 0, "Fake Weaviate received no projection documents.");
        Ensure(services.Indexer.Indexed.All(item => allowed.Contains(item.ArtifactType)), "Fake Weaviate received an unapproved artifact type.");
        Ensure(!Enum.GetNames<MemoryIndexArtifactType>().Any(name => name.Contains("AgentLocalMemoryItem", StringComparison.OrdinalIgnoreCase)), "AgentLocalMemoryItem index artifact type exists.");
        Ensure(!JsonSerializer.Serialize(services.Indexer.Indexed).Contains("ContentJson", StringComparison.OrdinalIgnoreCase), "Raw local memory ContentJson appeared in index projection payload.");

        return ["Fake Weaviate received only approved projection artifact types", "No AgentLocalMemoryItem artifact type exists", "No ContentJson appeared in indexed projections"];
    }

    private async Task<IReadOnlyList<string>> RunWeaviateReviewedPositiveDoesNotPromoteMemoryAsync(HarnessServices services, CancellationToken cancellationToken)
    {
        var silo = OpenSilo(services, "builder-agent");
        await silo.CreateAsync(BuildMemoryDraft("memory-reviewed-positive"), cancellationToken).ConfigureAwait(false);
        await services.ProposalService.CreateAsync(BuildProposalDraft("proposal-reviewed-positive", MemorySource("memory-reviewed-positive")), cancellationToken).ConfigureAwait(false);
        await services.ProposalService.AddEventAsync(BuildScope(), BuildProposalEvent("proposal-reviewed-positive", MemoryImprovementProposalEventType.AcceptedForFutureImplementation), cancellationToken).ConfigureAwait(false);

        await services.IndexingService.QueueRunAsync("tenant-1", "project-1", "campaign-1", "run-1", cancellationToken).ConfigureAwait(false);
        await services.IndexingService.ProcessPendingAsync("tenant-1", "project-1", 100, cancellationToken).ConfigureAwait(false);

        var reviewedPositive = services.Indexer.Indexed.Single(item => item.ArtifactId == "proposal-reviewed-positive" && item.ArtifactType == MemoryIndexArtifactType.MemoryImprovementProposal);
        Ensure(reviewedPositive.AuthorityLevel == MemoryIndexAuthorityLevel.ReviewedPositive, "Accepted proposal was not indexed as ReviewedPositive.");
        Ensure(await CountAcceptedOrSystemRuleMemoryAsync(cancellationToken).ConfigureAwait(false) == 0, "ReviewedPositive indexing created Accepted/SystemRule memory.");
        Ensure(await GetMemoryAuthorityAsync("memory-reviewed-positive", cancellationToken).ConfigureAwait(false) == MemoryAuthorityLevel.ObservedOnly, "ReviewedPositive indexing changed source memory authority.");

        return ["Proposal projection authority = ReviewedPositive", "Accepted/SystemRule count = 0", "Source memory authority stayed ObservedOnly"];
    }

    private async Task<IReadOnlyList<string>> RunSourceMutationNeverAllowedByMemoryAloneAsync(HarnessServices services, CancellationToken cancellationToken)
    {
        var silo = OpenSilo(services, "builder-agent");
        await silo.CreateAsync(BuildMemoryDraft("memory-source-mutation"), cancellationToken).ConfigureAwait(false);
        await silo.RecordInfluenceAsync(BuildInfluenceDraft("influence-source-mutation", "memory-source-mutation", "decision-1"), cancellationToken).ConfigureAwait(false);

        var result = await services.Governance.CheckAsync(BuildMemoryRequest("memory-source-mutation", MemoryGovernanceActionType.SourceMutation, influenceId: "influence-source-mutation"), cancellationToken).ConfigureAwait(false);
        EnsureIssue(result, MemoryGovernanceDecision.Warn, MemoryGovernanceIssueCode.SourceMutationRequiresApprovalBeyondMemory);

        return ["SourceMutation returned Warn, not Allow", "Issue SourceMutationRequiresApprovalBeyondMemory observed"];
    }

    private async Task<IReadOnlyList<string>> RunExternalEffectNeverAllowedByMemoryAloneAsync(HarnessServices services, CancellationToken cancellationToken)
    {
        var silo = OpenSilo(services, "builder-agent");
        await silo.CreateAsync(BuildMemoryDraft("memory-external-effect"), cancellationToken).ConfigureAwait(false);
        await silo.RecordInfluenceAsync(BuildInfluenceDraft("influence-external-effect", "memory-external-effect", "decision-1"), cancellationToken).ConfigureAwait(false);

        var result = await services.Governance.CheckAsync(BuildMemoryRequest("memory-external-effect", MemoryGovernanceActionType.ExternalEffect, influenceId: "influence-external-effect"), cancellationToken).ConfigureAwait(false);
        EnsureIssue(result, MemoryGovernanceDecision.Warn, MemoryGovernanceIssueCode.ExternalEffectRequiresApprovalBeyondMemory);

        return ["ExternalEffect returned Warn, not Allow", "Issue ExternalEffectRequiresApprovalBeyondMemory observed"];
    }

    private async Task<IReadOnlyList<string>> RunRawReasoningRejectedEverywhereAsync(HarnessServices services, CancellationToken cancellationToken)
    {
        var silo = OpenSilo(services, "builder-agent");
        await silo.CreateAsync(BuildMemoryDraft("memory-raw-reasoning-source"), cancellationToken).ConfigureAwait(false);

        await ExpectExceptionAsync<InvalidOperationException>(() => services.ProposalService.CreateAsync(
            BuildProposalDraft("proposal-raw-json", MemorySource("memory-raw-reasoning-source")) with
            {
                ProposalJson = "{\"RawPrompt\":\"secret\"}"
            }, cancellationToken)).ConfigureAwait(false);

        await services.ProposalService.CreateAsync(BuildProposalDraft("proposal-valid-for-raw-event", MemorySource("memory-raw-reasoning-source")), cancellationToken).ConfigureAwait(false);
        await ExpectExceptionAsync<InvalidOperationException>(() => services.ProposalService.AddEventAsync(
            BuildScope(),
            BuildProposalEvent("proposal-valid-for-raw-event", MemoryImprovementProposalEventType.AcceptedForFutureImplementation) with
            {
                EventJson = "{\"Scratchpad\":\"secret\"}"
            }, cancellationToken)).ConfigureAwait(false);

        foreach (var token in BannedPrivateReasoningTokens)
        {
            await ExpectExceptionAsync<InvalidOperationException>(() => services.QueueStore.QueueAsync(
                BuildProjection($"idx-raw-title-{token}", MemoryIndexArtifactType.RunMemoryReport) with { Title = $"Unsafe {token}" }, cancellationToken)).ConfigureAwait(false);
            await ExpectExceptionAsync<InvalidOperationException>(() => services.QueueStore.QueueAsync(
                BuildProjection($"idx-raw-summary-{token}", MemoryIndexArtifactType.RunMemoryReport) with { Summary = $"Unsafe {token}" }, cancellationToken)).ConfigureAwait(false);
            await ExpectExceptionAsync<InvalidOperationException>(() => services.QueueStore.QueueAsync(
                BuildProjection($"idx-raw-metadata-{token}", MemoryIndexArtifactType.RunMemoryReport) with { Metadata = new Dictionary<string, string> { ["marker"] = token } }, cancellationToken)).ConfigureAwait(false);
        }

        Ensure(services.Indexer.Indexed.Count == 0, "Raw reasoning rejection scenario called fake Weaviate.");

        return ["ProposalJson raw marker rejected", "EventJson raw marker rejected", "Index title/summary/metadata raw markers rejected", "No fake Weaviate calls occurred"];
    }

    private async Task<IReadOnlyList<string>> RunAppendOnlyMutationBlockedAsync(HarnessServices services, CancellationToken cancellationToken)
    {
        var silo = OpenSilo(services, "builder-agent");
        await silo.CreateAsync(BuildMemoryDraft("memory-append-only"), cancellationToken).ConfigureAwait(false);
        await silo.RecordInfluenceAsync(BuildInfluenceDraft("influence-append-only", "memory-append-only", "decision-1"), cancellationToken).ConfigureAwait(false);
        await silo.CreateHandoffAsync(BuildHandoffDraft("handoff-append-only", "tester-agent", ["memory-append-only"]), cancellationToken).ConfigureAwait(false);
        await services.ProposalService.CreateAsync(BuildProposalDraft("proposal-append-only", MemorySource("memory-append-only")), cancellationToken).ConfigureAwait(false);
        await services.QueueStore.QueueAsync(BuildProjection("idx-append-only", MemoryIndexArtifactType.RunMemoryReport), cancellationToken).ConfigureAwait(false);

        var sqlStatements = new[]
        {
            "UPDATE agent.AgentLocalMemoryItem SET Title = 'mutated' WHERE MemoryItemId = 'memory-append-only';",
            "DELETE FROM agent.AgentLocalMemoryItem WHERE MemoryItemId = 'memory-append-only';",
            "UPDATE agent.AgentLocalMemoryEvidenceRef SET Summary = 'mutated' WHERE MemoryItemId = 'memory-append-only';",
            "DELETE FROM agent.AgentLocalMemoryEvidenceRef WHERE MemoryItemId = 'memory-append-only';",
            "UPDATE agent.AgentLocalMemoryEvent SET EventReason = 'mutated' WHERE MemoryItemId = 'memory-append-only';",
            "DELETE FROM agent.AgentLocalMemoryEvent WHERE MemoryItemId = 'memory-append-only';",
            "UPDATE agent.AgentMemoryInfluenceRecord SET InfluenceSummary = 'mutated' WHERE InfluenceId = 'influence-append-only';",
            "DELETE FROM agent.AgentMemoryInfluenceRecord WHERE InfluenceId = 'influence-append-only';",
            "UPDATE agent.AgentMemoryHandoffSlice SET Summary = 'mutated' WHERE HandoffMemorySliceId = 'handoff-append-only';",
            "DELETE FROM agent.AgentMemoryHandoffSlice WHERE HandoffMemorySliceId = 'handoff-append-only';",
            "UPDATE agent.AgentMemoryImprovementProposal SET Title = 'mutated' WHERE ProposalId = 'proposal-append-only';",
            "DELETE FROM agent.AgentMemoryImprovementProposal WHERE ProposalId = 'proposal-append-only';",
            "UPDATE agent.AgentMemoryImprovementProposalEvent SET Reason = 'mutated' WHERE ProposalId = 'proposal-append-only';",
            "DELETE FROM agent.AgentMemoryImprovementProposalEvent WHERE ProposalId = 'proposal-append-only';",
            "UPDATE agent.AgentMemoryIndexQueue SET Title = 'mutated' WHERE IndexRecordId = 'idx-append-only';",
            "DELETE FROM agent.AgentMemoryIndexQueue WHERE IndexRecordId = 'idx-append-only';",
            "UPDATE agent.AgentMemoryIndexEvent SET Error = 'mutated' WHERE IndexRecordId = 'idx-append-only';",
            "DELETE FROM agent.AgentMemoryIndexEvent WHERE IndexRecordId = 'idx-append-only';"
        };

        foreach (var sql in sqlStatements)
            await ExpectSqlFailsAsync(sql, cancellationToken).ConfigureAwait(false);

        return [$"Append-only SQL update/delete blockers fired for {sqlStatements.Length} mutation attempts"];
    }

    private async Task<IReadOnlyList<string>> RunRunReportDoesNotLeakOtherRunAsync(HarnessServices services, CancellationToken cancellationToken)
    {
        var run1 = OpenSilo(services, "builder-agent", runId: "run-1");
        await run1.CreateAsync(BuildMemoryDraft("memory-report-run-1"), cancellationToken).ConfigureAwait(false);
        await run1.RecordInfluenceAsync(BuildInfluenceDraft("influence-report-run-1", "memory-report-run-1", "decision-1"), cancellationToken).ConfigureAwait(false);
        await run1.CreateHandoffAsync(BuildHandoffDraft("handoff-report-run-1", "tester-agent", ["memory-report-run-1"]), cancellationToken).ConfigureAwait(false);

        var run2 = OpenSilo(services, "builder-agent", runId: "run-2");
        await run2.CreateAsync(BuildMemoryDraft("memory-report-run-2"), cancellationToken).ConfigureAwait(false);
        await run2.RecordInfluenceAsync(BuildInfluenceDraft("influence-report-run-2", "memory-report-run-2", "decision-1"), cancellationToken).ConfigureAwait(false);
        await run2.CreateHandoffAsync(BuildHandoffDraft("handoff-report-run-2", "tester-agent", ["memory-report-run-2"]), cancellationToken).ConfigureAwait(false);

        var report = await services.RunReportService.BuildAsync(new RunMemoryReportRequest
        {
            TenantId = "tenant-1",
            ProjectId = "project-1",
            CampaignId = "campaign-1",
            RunId = "run-1",
            TakePerAgent = 100
        }, cancellationToken).ConfigureAwait(false);

        var serialized = JsonSerializer.Serialize(report);
        Ensure(report.RunId == "run-1", "Run report returned wrong run ID.");
        Ensure(report.TotalMemoryItemsCreated == 1, "Run report memory total leaked another run.");
        Ensure(report.TotalInfluenceRecords == 1, "Run report influence total leaked another run.");
        Ensure(report.TotalHandoffSlices == 1, "Run report handoff total leaked another run.");
        Ensure(!serialized.Contains("run-2", StringComparison.OrdinalIgnoreCase), "Run report leaked run-2 identifier.");
        Ensure(!serialized.Contains("memory-report-run-2", StringComparison.OrdinalIgnoreCase), "Run report leaked run-2 memory.");
        Ensure(!serialized.Contains("influence-report-run-2", StringComparison.OrdinalIgnoreCase), "Run report leaked run-2 influence.");
        Ensure(!serialized.Contains("handoff-report-run-2", StringComparison.OrdinalIgnoreCase), "Run report leaked run-2 handoff.");

        return ["Run report scoped to run-1", "Totals match run-1 only", "No run-2 artifact IDs appeared"];
    }

    private Task<IReadOnlyList<string>> RunSiloDoesNotExposeGovernanceOrIndexingServicesAsync(HarnessServices services, CancellationToken cancellationToken)
    {
        _ = services;
        _ = cancellationToken;
        string[] banned = ["Conscience", "Governance", "Proposal", "Index", "Weaviate", "RunReport"];
        var methods = typeof(IAgentMemorySilo).GetMethods(BindingFlags.Instance | BindingFlags.Public);

        foreach (var method in methods)
        {
            foreach (var token in banned)
            {
                Ensure(!method.Name.Contains(token, StringComparison.OrdinalIgnoreCase), $"IAgentMemorySilo method name exposes {token}.");
                Ensure(!method.ReturnType.Name.Contains(token, StringComparison.OrdinalIgnoreCase), $"IAgentMemorySilo return type exposes {token}.");
                Ensure(!method.GetParameters().Any(parameter => parameter.ParameterType.Name.Contains(token, StringComparison.OrdinalIgnoreCase)), $"IAgentMemorySilo parameter type exposes {token}.");
            }
        }

        return Task.FromResult<IReadOnlyList<string>>(["IAgentMemorySilo exposes no Conscience/Governance/Proposal/Index/Weaviate/RunReport method or type names"]);
    }

    private async Task ResetSchemaAsync(CancellationToken cancellationToken)
    {
        await DropAgentMemorySchemaAsync(cancellationToken).ConfigureAwait(false);
        await ApplyAgentMemoryMigrationsAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task ApplyAgentMemoryMigrationsAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(await File.ReadAllTextAsync(Path.Combine(_repositoryRoot, "Database", "migrate_agent_local_memory.sql"), cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken)).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(await File.ReadAllTextAsync(Path.Combine(_repositoryRoot, "Database", "migrate_agent_memory_influence.sql"), cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken)).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(await File.ReadAllTextAsync(Path.Combine(_repositoryRoot, "Database", "migrate_agent_memory_handoff.sql"), cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken)).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(await File.ReadAllTextAsync(Path.Combine(_repositoryRoot, "Database", "migrate_agent_memory_improvement_proposals.sql"), cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken)).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(await File.ReadAllTextAsync(Path.Combine(_repositoryRoot, "Database", "migrate_agent_memory_execution_audit.sql"), cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken)).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(await File.ReadAllTextAsync(Path.Combine(_repositoryRoot, "Database", "migrate_agent_memory_indexing.sql"), cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private async Task DropAgentMemorySchemaAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            IF OBJECT_ID('agent.TR_AgentMemoryExecutionAudit_ValidateInsert', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentMemoryExecutionAudit_ValidateInsert;
            IF OBJECT_ID('agent.TR_AgentMemoryExecutionAudit_BlockUpdateDelete', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentMemoryExecutionAudit_BlockUpdateDelete;
            IF OBJECT_ID('agent.AgentMemoryExecutionAudit', 'U') IS NOT NULL
                DROP TABLE agent.AgentMemoryExecutionAudit;
            IF OBJECT_ID('agent.TR_AgentMemoryIndexEvent_ValidateInsert', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentMemoryIndexEvent_ValidateInsert;
            IF OBJECT_ID('agent.TR_AgentMemoryIndexQueue_ValidateProjection', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentMemoryIndexQueue_ValidateProjection;
            IF OBJECT_ID('agent.TR_AgentMemoryIndexEvent_BlockUpdateDelete', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentMemoryIndexEvent_BlockUpdateDelete;
            IF OBJECT_ID('agent.TR_AgentMemoryIndexQueue_BlockUpdateDelete', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentMemoryIndexQueue_BlockUpdateDelete;
            IF OBJECT_ID('agent.AgentMemoryIndexEvent', 'U') IS NOT NULL
                DROP TABLE agent.AgentMemoryIndexEvent;
            IF OBJECT_ID('agent.AgentMemoryIndexQueue', 'U') IS NOT NULL
                DROP TABLE agent.AgentMemoryIndexQueue;
            IF OBJECT_ID('agent.TR_AgentMemoryImprovementProposalEvent_ValidateInsert', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentMemoryImprovementProposalEvent_ValidateInsert;
            IF OBJECT_ID('agent.TR_AgentMemoryImprovementProposal_ValidateSources', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentMemoryImprovementProposal_ValidateSources;
            IF OBJECT_ID('agent.TR_AgentMemoryImprovementProposalEvent_BlockUpdateDelete', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentMemoryImprovementProposalEvent_BlockUpdateDelete;
            IF OBJECT_ID('agent.TR_AgentMemoryImprovementProposal_BlockUpdateDelete', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentMemoryImprovementProposal_BlockUpdateDelete;
            IF OBJECT_ID('agent.AgentMemoryImprovementProposalEvent', 'U') IS NOT NULL
                DROP TABLE agent.AgentMemoryImprovementProposalEvent;
            IF OBJECT_ID('agent.AgentMemoryImprovementProposal', 'U') IS NOT NULL
                DROP TABLE agent.AgentMemoryImprovementProposal;
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
            """,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private IAgentMemorySilo OpenSilo(
        HarnessServices services,
        string agentId,
        string tenantId = "tenant-1",
        string projectId = "project-1",
        string campaignId = "campaign-1",
        string runId = "run-1") =>
        services.Silo.Open(BuildContext(tenantId, projectId, campaignId, runId, agentId));

    private static AgentMemorySiloContext BuildContext(
        string tenantId,
        string projectId,
        string campaignId,
        string runId,
        string agentId) =>
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

    private static AgentMemoryScope BuildScope(
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
            AgentId = agentId
        };

    private static AgentLocalMemoryDraft BuildMemoryDraft(string memoryItemId) =>
        new()
        {
            MemoryItemId = memoryItemId,
            MemoryType = AgentMemoryType.Episodic,
            AuthorityLevel = MemoryAuthorityLevel.ObservedOnly,
            Title = "Observed build failure",
            Summary = "TesterAgent observed a reproducible build failure.",
            EvidenceRefs = [BuildEvidence($"evidence-{memoryItemId}")],
            Confidence = 0.8m,
            CreatedAt = Now
        };

    private static AgentLocalMemoryEventDraft BuildEventDraft(
        string memoryItemId,
        AgentLocalMemoryEventType eventType,
        int minutesAfterCreated) =>
        new()
        {
            MemoryEventId = $"event-{memoryItemId}-{eventType}-{minutesAfterCreated}",
            MemoryItemId = memoryItemId,
            EventType = eventType,
            EventReason = $"Lifecycle event {eventType}.",
            CreatedAt = Now.AddMinutes(minutesAfterCreated),
            CreatedByUserId = "human-reviewer",
            DecisionId = "decision-event",
            ThoughtLedgerEntryId = "thought-1",
            EventJson = "{}"
        };

    private static MemoryInfluenceDraft BuildInfluenceDraft(
        string influenceId,
        string memoryItemId,
        string decisionId) =>
        new()
        {
            InfluenceId = influenceId,
            MemoryItemId = memoryItemId,
            DecisionId = decisionId,
            InfluenceType = MemoryInfluenceType.ToolCallJustified,
            InfluenceSummary = "Memory was used to justify a governed tool call.",
            EvidenceRefs = [BuildEvidence($"evidence-{influenceId}")],
            Confidence = 0.8m,
            CreatedAt = Now.AddMinutes(5),
            ThoughtLedgerEntryId = $"thought-{influenceId}",
            CorrelationId = "correlation-1"
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
            Confidence = 0.8m,
            CreatedAt = Now.AddMinutes(10),
            ThoughtLedgerEntryId = $"thought-{handoffId}",
            CorrelationId = "correlation-1",
            HandoffJson = "{}"
        };

    private static MemoryImprovementProposalDraft BuildProposalDraft(
        string proposalId,
        MemoryImprovementProposalSource source,
        AgentMemoryScope? scope = null)
    {
        var boundScope = scope ?? BuildScope();
        return new MemoryImprovementProposalDraft
        {
            ProposalId = proposalId,
            Scope = boundScope,
            ProposalType = MemoryImprovementProposalType.PromoteObservedMemory,
            Title = "Consider promoting observed memory",
            Summary = "The observed memory has enough evidence to deserve later human review.",
            Sources = [source],
            EvidenceRefs = [BuildEvidence($"evidence-{proposalId}") with { EvidenceType = EvidenceType.RunReport }],
            Confidence = 0.72m,
            CreatedAt = Now.AddMinutes(20),
            ProposedByAgentId = boundScope.AgentId,
            CorrelationId = "correlation-1",
            ThoughtLedgerEntryId = "thought-proposal",
            ProposalJson = "{}"
        };
    }

    private static MemoryImprovementProposalEventDraft BuildProposalEvent(
        string proposalId,
        MemoryImprovementProposalEventType eventType) =>
        new()
        {
            ProposalEventId = $"event-{proposalId}-{eventType}",
            ProposalId = proposalId,
            EventType = eventType,
            CreatedAt = Now.AddMinutes(30),
            Reason = $"Proposal reviewed as {eventType}.",
            CreatedByUserId = "human-reviewer",
            ThoughtLedgerEntryId = "thought-review",
            CorrelationId = "correlation-1",
            EventJson = "{}"
        };

    private static MemoryImprovementProposalSource MemorySource(string memoryItemId) =>
        new() { MemoryItemId = memoryItemId, ThoughtLedgerEntryId = "thought-memory" };

    private static MemoryGovernanceCheckRequest BuildMemoryRequest(
        string memoryItemId,
        MemoryGovernanceActionType actionType,
        AgentMemoryScope? scope = null,
        string? influenceId = null,
        bool influenceRequired = true) =>
        new()
        {
            Scope = scope ?? BuildScope(),
            ActionType = actionType,
            DecisionId = "decision-1",
            ReferencedArtifacts =
            [
                new MemoryGovernanceReferencedArtifact
                {
                    MemoryItemId = memoryItemId,
                    InfluenceId = influenceId,
                    DecisionId = "decision-1",
                    ThoughtLedgerEntryId = "thought-request-1"
                }
            ],
            RequestedAt = Now,
            ToolName = actionType == MemoryGovernanceActionType.ToolCallJustification ? "test.run" : null,
            AffectedArtifactType = actionType == MemoryGovernanceActionType.SourceMutation ? "source" : actionType == MemoryGovernanceActionType.ExternalEffect ? "external" : null,
            AffectedArtifactId = actionType == MemoryGovernanceActionType.SourceMutation ? "file.cs" : actionType == MemoryGovernanceActionType.ExternalEffect ? "github" : null,
            CorrelationId = "correlation-1",
            InfluenceRecordRequired = influenceRequired
        };

    private static AgentSkillExecutionRequest BuildAuditExecutionRequest(MemoryBackedExecutionContext memoryContext) =>
        new()
        {
            SkillRequestContext = new AgentSkillRequestContext
            {
                ContextId = "skill-context-execution-audit",
                RequestId = "skill-request-execution-audit",
                ReviewId = "skill-review-execution-audit",
                ProjectId = "IronDev",
                AgentName = "BuilderAgent",
                SkillId = AgentSkillIds.WorkspaceValidate,
                Purpose = "Validate disposable workspace using memory-backed context.",
                SkillKnown = true,
                Decision = ProjectApprovalDecisions.AllowedByPolicy,
                ReviewStatus = AgentSkillRequestReviewStatuses.ApprovedForExecution,
                RiskTier = ProjectApprovalRiskTiers.WorkspaceValidation,
                Category = AgentSkillCategories.WorkspaceCommand,
                HumanReviewRequired = true,
                HumanApprovalRequired = false,
                PolicyAllowed = true,
                PolicyBlocked = false,
                DangerousCapability = false,
                ExecutionCanStartFromContext = true,
                ApprovalCanBeGrantedByContext = false,
                SourceMutationAllowed = false,
                WorkspaceMutationAllowed = true,
                ExternalSystemAllowed = false,
                CreatesTicketAllowed = false,
                WritesMemoryAllowed = false,
                RecommendedNextAction = AgentSkillRequestContextRecommendedActions.ExecuteApprovedRequest,
                EvidencePaths = ["workspace-validation-context.json"],
                ParametersSummary = ["runId=run-1", "workspacePath=C:\\workspaces\\run-1"],
                ReviewChecklist = ["Confirm memory-backed execution audit is durable."],
                Blockers = [],
                Warnings = [],
                Interpretation = ["Approved non-source-mutating workspace command."]
            },
            RequestedByAgent = "BuilderAgent",
            ProjectId = "IronDev",
            RunId = "run-1",
            WorkspacePath = "C:\\workspaces\\run-1",
            SourceRepo = "C:\\repo\\IronDeveloper",
            MemoryExecutionContext = memoryContext,
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["runId"] = "run-1",
                ["workspacePath"] = "C:\\workspaces\\run-1"
            }
        };

    private static AgentSkillExecutionResult BuildAuditExecutionResult(MemoryExecutionEvidence evidence) =>
        new()
        {
            ExecutionId = "skill-execution-audit-1",
            ContextId = "skill-context-execution-audit",
            RequestId = "skill-request-execution-audit",
            ReviewId = "skill-review-execution-audit",
            SkillId = AgentSkillIds.WorkspaceValidate,
            Status = AgentSkillExecutionStatuses.Succeeded,
            Summary = "Workspace validation completed.",
            Executed = true,
            ReadOnlyExecution = false,
            SourceMutated = false,
            WorkspaceMutated = true,
            ExternalSystemCalled = false,
            TicketCreated = false,
            MemoryWritten = false,
            ApprovalGranted = false,
            ShellCommandRun = false,
            Payload = null,
            MemoryEvidence = evidence,
            EvidencePaths = ["workspace-validation.json"],
            Warnings = [],
            Blockers = []
        };

    private static MemoryGovernanceCheckRequest BuildInfluenceOnlyRequest(
        string influenceId,
        MemoryGovernanceActionType actionType) =>
        new()
        {
            Scope = BuildScope(),
            ActionType = actionType,
            DecisionId = "decision-1",
            ReferencedArtifacts =
            [
                new MemoryGovernanceReferencedArtifact
                {
                    InfluenceId = influenceId,
                    DecisionId = "decision-1",
                    ThoughtLedgerEntryId = "thought-request-1"
                }
            ],
            RequestedAt = Now,
            ToolName = actionType == MemoryGovernanceActionType.ToolCallJustification ? "test.run" : null,
            CorrelationId = "correlation-1"
        };

    private static MemoryGovernanceCheckRequest BuildHandoffRequest(
        string handoffId,
        MemoryGovernanceActionType actionType,
        AgentMemoryScope scope) =>
        new()
        {
            Scope = scope,
            ActionType = actionType,
            DecisionId = "decision-1",
            ReferencedArtifacts =
            [
                new MemoryGovernanceReferencedArtifact
                {
                    HandoffMemorySliceId = handoffId,
                    DecisionId = "decision-1",
                    ThoughtLedgerEntryId = "thought-handoff-use"
                }
            ],
            RequestedAt = Now,
            TargetAgentId = scope.AgentId,
            CorrelationId = "correlation-1",
            InfluenceRecordRequired = false
        };

    private static MemoryIndexProjection BuildProjection(string indexRecordId, MemoryIndexArtifactType artifactType) =>
        new()
        {
            IndexRecordId = indexRecordId,
            TenantId = "tenant-1",
            ProjectId = "project-1",
            CampaignId = "campaign-1",
            RunId = "run-1",
            AgentId = "builder-agent",
            ArtifactType = artifactType,
            ArtifactId = indexRecordId.Replace("idx-", "artifact-", StringComparison.OrdinalIgnoreCase),
            AuthorityLevel = MemoryIndexAuthorityLevel.ObservedProjection,
            Title = "Safe memory index projection",
            Summary = "Projection summarizes governed memory evidence without raw reasoning.",
            EvidenceRefs = [BuildEvidence($"evidence-{indexRecordId}")],
            CreatedAt = Now,
            DecisionId = $"decision-{indexRecordId}",
            ThoughtLedgerEntryId = $"thought-{indexRecordId}",
            CorrelationId = "correlation-1",
            Metadata = new Dictionary<string, string> { ["projectionKind"] = artifactType.ToString() },
            SourceHashSha256 = new string('a', 64)
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

    private async Task<int> CountMemoryEventsAsync(string memoryItemId, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await connection.QuerySingleAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM agent.AgentLocalMemoryEvent WHERE MemoryItemId = @MemoryItemId;",
            new { MemoryItemId = memoryItemId },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private async Task<int> CountAcceptedOrSystemRuleMemoryAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await connection.QuerySingleAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(*)
            FROM agent.AgentLocalMemoryItem
            WHERE AuthorityLevel IN (@Accepted, @SystemRule);
            """,
            new
            {
                Accepted = (int)MemoryAuthorityLevel.Accepted,
                SystemRule = (int)MemoryAuthorityLevel.SystemRule
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private async Task<MemoryAuthorityLevel> GetMemoryAuthorityAsync(string memoryItemId, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var value = await connection.QuerySingleAsync<int>(new CommandDefinition(
            "SELECT AuthorityLevel FROM agent.AgentLocalMemoryItem WHERE MemoryItemId = @MemoryItemId;",
            new { MemoryItemId = memoryItemId },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        return (MemoryAuthorityLevel)value;
    }

    private async Task<AgentLocalMemoryEventType> GetCurrentMemoryEventTypeAsync(string memoryItemId, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var value = await connection.QuerySingleAsync<int>(new CommandDefinition(
            "SELECT ISNULL(CurrentEventType, 1) FROM agent.vwAgentLocalMemoryCurrentState WHERE MemoryItemId = @MemoryItemId;",
            new { MemoryItemId = memoryItemId },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        return (AgentLocalMemoryEventType)value;
    }

    private async Task ExpectSqlFailsAsync(string sql, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(sql, cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
        catch (SqlException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected SQL mutation to fail but it succeeded: {sql}");
    }

    private static async Task ExpectExceptionAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected exception {typeof(TException).Name} was not thrown.");
    }

    private static void EnsureIssue(
        MemoryGovernanceCheckResult result,
        MemoryGovernanceDecision expectedDecision,
        MemoryGovernanceIssueCode issueCode)
    {
        Ensure(result.Decision == expectedDecision, $"Expected governance decision {expectedDecision}, got {result.Decision}.");
        Ensure(result.Issues.Any(issue => issue.Code == issueCode), $"Expected governance issue {issueCode}.");
    }

    private static void Ensure(bool condition, string failureReason)
    {
        if (!condition)
            throw new InvalidOperationException(failureReason);
    }

    private sealed record HarnessServices(
        AgentMemorySiloService Silo,
        SqlMemoryImprovementProposalService ProposalService,
        SqlAgentMemoryRunReportService RunReportService,
        SqlConscienceMemoryGovernanceService Governance,
        SqlMemoryExecutionAuditStore ExecutionAuditStore,
        SqlMemoryIndexQueueStore QueueStore,
        MemoryIndexingService IndexingService,
        FakeWeaviateMemoryIndexer Indexer);

    private sealed class FakeWeaviateMemoryIndexer : IWeaviateMemoryIndexer
    {
        public List<MemoryIndexProjection> Indexed { get; } = [];

        public Task<WeaviateMemoryIndexResult> IndexAsync(
            MemoryIndexProjection projection,
            CancellationToken cancellationToken = default)
        {
            Indexed.Add(projection);
            return Task.FromResult(new WeaviateMemoryIndexResult
            {
                Success = true,
                WeaviateObjectId = $"weaviate-{projection.IndexRecordId}"
            });
        }
    }
}
