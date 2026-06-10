using System.Reflection;
using IronDev.Api.Controllers;
using IronDev.Core.Agents;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Audit = IronDev.Core.Agents.Audit;

namespace IronDev.IntegrationTests.Agents;

[TestClass]
public sealed class AgentRunAuditApiTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 6, 10, 1, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void AgentRunAuditApi_ContractsExist()
    {
        Assert.AreEqual(nameof(Audit.AgentRunListItemDto), typeof(Audit.AgentRunListItemDto).Name);
        Assert.AreEqual(nameof(Audit.AgentRunDetailDto), typeof(Audit.AgentRunDetailDto).Name);
        Assert.AreEqual(nameof(Audit.AgentRunRecordDto), typeof(Audit.AgentRunRecordDto).Name);
        Assert.AreEqual(nameof(Audit.AgentDefinitionSnapshotDto), typeof(Audit.AgentDefinitionSnapshotDto).Name);
        Assert.AreEqual(nameof(Audit.AgentRunInputRefDto), typeof(Audit.AgentRunInputRefDto).Name);
        Assert.AreEqual(nameof(Audit.AgentRunOutputRefDto), typeof(Audit.AgentRunOutputRefDto).Name);
        Assert.AreEqual(nameof(Audit.AgentCapabilityUseDto), typeof(Audit.AgentCapabilityUseDto).Name);
        Assert.AreEqual(nameof(Audit.AgentBoundaryDecisionDto), typeof(Audit.AgentBoundaryDecisionDto).Name);
        Assert.AreEqual(nameof(Audit.ThoughtLedgerEntryDto), typeof(Audit.ThoughtLedgerEntryDto).Name);
        Assert.AreEqual(nameof(Audit.AgentRunStepDto), typeof(Audit.AgentRunStepDto).Name);
        Assert.AreEqual(nameof(Audit.AgentRunSafetySummaryDto), typeof(Audit.AgentRunSafetySummaryDto).Name);
        Assert.AreEqual(nameof(Audit.AgentRunAuditQueryResultDto), typeof(Audit.AgentRunAuditQueryResultDto).Name);
        Assert.AreEqual(nameof(Audit.AgentRunAuditQueryIssueDto), typeof(Audit.AgentRunAuditQueryIssueDto).Name);
        Assert.AreEqual(nameof(Audit.IAgentRunAuditQueryService), typeof(Audit.IAgentRunAuditQueryService).Name);
        Assert.AreEqual(nameof(Audit.IAgentRunAuditEnvelopeReadRepository), typeof(Audit.IAgentRunAuditEnvelopeReadRepository).Name);
        Assert.AreEqual(nameof(Audit.AgentRunAuditDtoMapper), typeof(Audit.AgentRunAuditDtoMapper).Name);
    }

    [TestMethod]
    public void AgentRunAuditApi_QueryServiceListsProjectScopedRunsWithPagingAndFilters()
    {
        var criticRun = BuildEnvelope("1", "agent-run-critic", AgentDefinitionCatalog.IndependentCriticAgent, Audit.AgentRunStatus.CompletedWithWarnings, Audit.AgentRunTriggerType.ManualUserRequest);
        var memoryRun = BuildEnvelope("1", "agent-run-memory", AgentDefinitionCatalog.MemoryImprovementAgent, Audit.AgentRunStatus.Completed, Audit.AgentRunTriggerType.TestHarness);
        var otherProject = BuildEnvelope("2", "agent-run-other-project", AgentDefinitionCatalog.IndependentCriticAgent, Audit.AgentRunStatus.Completed, Audit.AgentRunTriggerType.ManualUserRequest);
        var service = BuildService(criticRun, memoryRun, otherProject);

        var list = service.ListAgentRuns("1", new Audit.AgentRunAuditListQuery());

        Assert.AreEqual(2, list.TotalCount);
        Assert.AreEqual(2, list.Items.Count);
        Assert.IsFalse(list.Items.Any(item => item.AgentRunId == otherProject.Run.AgentRunId));
        Assert.AreEqual(1, service.ListAgentRuns("1", new Audit.AgentRunAuditListQuery { Take = 1, Skip = 1 }).Items.Count);
        Assert.AreEqual(1, service.ListAgentRuns("1", new Audit.AgentRunAuditListQuery { AgentId = AgentDefinitionCatalog.MemoryImprovementAgent.AgentId }).Items.Count);
        Assert.AreEqual(1, service.ListAgentRuns("1", new Audit.AgentRunAuditListQuery { AgentKind = AgentKind.ProposalAgent }).Items.Count);
        Assert.AreEqual(1, service.ListAgentRuns("1", new Audit.AgentRunAuditListQuery { Status = Audit.AgentRunStatus.CompletedWithWarnings }).Items.Count);
        Assert.AreEqual(1, service.ListAgentRuns("1", new Audit.AgentRunAuditListQuery { TriggerType = Audit.AgentRunTriggerType.TestHarness }).Items.Count);
    }

    [TestMethod]
    public void AgentRunAuditApi_QueryServiceRejectsInvalidListQuery()
    {
        var service = BuildService(BuildEnvelope("1", "agent-run-1", AgentDefinitionCatalog.IndependentCriticAgent));

        AssertHasIssue(
            service.ListAgentRuns(string.Empty, new Audit.AgentRunAuditListQuery()).Issues,
            Audit.AgentRunAuditQueryService.ProjectIdRequired);
        AssertHasIssue(
            service.ListAgentRuns("1", new Audit.AgentRunAuditListQuery { Take = 201 }).Issues,
            Audit.AgentRunAuditQueryService.TakeOutOfRange);
        AssertHasIssue(
            service.ListAgentRuns("1", new Audit.AgentRunAuditListQuery { Skip = -1 }).Issues,
            Audit.AgentRunAuditQueryService.SkipOutOfRange);
        AssertHasIssue(
            service.ListAgentRuns("1", new Audit.AgentRunAuditListQuery { FromUtc = CreatedAt.AddDays(1), ToUtc = CreatedAt }).Issues,
            Audit.AgentRunAuditQueryService.DateRangeInvalid);
    }

    [TestMethod]
    public void AgentRunAuditApi_QueryServiceReturnsDetailsAndChildViews()
    {
        var envelope = BuildEnvelope("1", "agent-run-1", AgentDefinitionCatalog.IndependentCriticAgent);
        var service = BuildService(envelope);

        var detail = service.GetAgentRun("1", "agent-run-1");

        Assert.IsNotNull(detail.Run);
        Assert.AreEqual("agent-run-1", detail.Run.Run.AgentRunId);
        Assert.AreEqual(1, service.GetThoughtLedger("1", "agent-run-1").Items.Count);
        Assert.AreEqual(2, service.GetCapabilities("1", "agent-run-1").Items.Count);
        Assert.AreEqual(1, service.GetBoundaryDecisions("1", "agent-run-1").Items.Count);
        Assert.AreEqual(1, service.GetOutputs("1", "agent-run-1").Items.Count);
        Assert.AreEqual(1, service.GetInputs("1", "agent-run-1").Items.Count);
    }

    [TestMethod]
    public void AgentRunAuditApi_QueryServiceBlocksCrossProjectAndMissingRuns()
    {
        var service = BuildService(BuildEnvelope("1", "agent-run-1", AgentDefinitionCatalog.IndependentCriticAgent));

        AssertHasIssue(
            service.GetAgentRun("2", "agent-run-1").Issues,
            Audit.AgentRunAuditQueryService.AgentRunNotFound);
        AssertHasIssue(
            service.GetAgentRun("1", "missing-run").Issues,
            Audit.AgentRunAuditQueryService.AgentRunNotFound);
        AssertHasIssue(
            service.GetAgentRun("1", string.Empty).Issues,
            Audit.AgentRunAuditQueryService.AgentRunIdRequired);
    }

    [TestMethod]
    public void AgentRunAuditApi_MapperPreservesSafeFieldsAndSafetyFlags()
    {
        var envelope = BuildEnvelope("1", "agent-run-1", AgentDefinitionCatalog.IndependentCriticAgent);

        var detail = Audit.AgentRunAuditDtoMapper.ToDetail(envelope);

        Assert.AreEqual("agent-run-1", detail.Run.AgentRunId);
        Assert.AreEqual(AgentDefinitionCatalog.IndependentCriticAgent.AgentId, detail.AgentDefinition.AgentId);
        Assert.AreEqual(AgentDefinitionCatalog.IndependentCriticAgent.Persona!.DisplayName, detail.AgentDefinition.PersonaDisplayName);
        Assert.AreEqual(1, detail.Inputs.Count);
        Assert.AreEqual(1, detail.Outputs.Count);
        Assert.AreEqual(2, detail.CapabilityUses.Count);
        Assert.AreEqual(1, detail.BoundaryDecisions.Count);
        Assert.AreEqual(1, detail.ThoughtLedger.Count);
        Assert.AreEqual(1, detail.Steps.Count);
        Assert.IsTrue(detail.Outputs[0].IsReviewOnly);
        Assert.IsFalse(detail.Outputs[0].CreatesAuthority);
        Assert.IsFalse(detail.Outputs[0].CreatesRuntimeAction);
        Assert.IsTrue(detail.SafetySummary.HasBlockedCapabilityAttempt);
        Assert.IsTrue(detail.SafetySummary.HasBoundaryBlock);
        Assert.IsFalse(detail.SafetySummary.HasAuthorityCreatingOutput);
    }

    [TestMethod]
    public void AgentRunAuditApi_MapperRedactsRawPrivateReasoningWithoutHidingFlag()
    {
        var envelope = BuildEnvelope("1", "agent-run-1", AgentDefinitionCatalog.IndependentCriticAgent) with
        {
            Inputs =
            [
                BuildInput("agent-run-1") with
                {
                    Summary = "RawPrompt: do not expose this.",
                    ContainsRawPrivateReasoning = true
                }
            ],
            ThoughtLedger =
            [
                BuildThought("agent-run-1") with
                {
                    Summary = "Scratchpad details should not be exposed.",
                    ContainsRawPrivateReasoning = true
                }
            ]
        };

        var detail = Audit.AgentRunAuditDtoMapper.ToDetail(envelope);

        Assert.AreEqual("[redacted unsafe audit text]", detail.Inputs[0].Summary);
        Assert.AreEqual("[redacted unsafe audit text]", detail.ThoughtLedger[0].Summary);
        Assert.IsTrue(detail.Inputs[0].ContainsRawPrivateReasoning);
        Assert.IsTrue(detail.ThoughtLedger[0].ContainsRawPrivateReasoning);
        Assert.IsTrue(detail.SafetySummary.ContainsRawPrivateReasoning);
        AssertNoRawMarkers(detail.Inputs[0].Summary);
        AssertNoRawMarkers(detail.ThoughtLedger[0].Summary);
    }

    [TestMethod]
    public void AgentRunAuditApi_ControllerExposesOnlyReadEndpoints()
    {
        var controllerType = typeof(AgentRunAuditController);
        var methods = controllerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
        var routePrefix = controllerType.GetCustomAttribute<RouteAttribute>()?.Template ?? string.Empty;

        Assert.AreEqual("api/projects/{projectId:int}/agent-runs", routePrefix);
        Assert.IsTrue(methods.Any(method => method.Name == "List" && method.GetCustomAttribute<HttpGetAttribute>() is not null));
        Assert.IsTrue(methods.Any(method => method.Name == "Get" && method.GetCustomAttribute<HttpGetAttribute>()?.Template == "{agentRunId}"));
        Assert.IsTrue(methods.Any(method => method.Name == "GetThoughtLedger" && method.GetCustomAttribute<HttpGetAttribute>()?.Template == "{agentRunId}/thought-ledger"));
        Assert.IsTrue(methods.Any(method => method.Name == "GetCapabilities" && method.GetCustomAttribute<HttpGetAttribute>()?.Template == "{agentRunId}/capabilities"));
        Assert.IsTrue(methods.Any(method => method.Name == "GetBoundaries" && method.GetCustomAttribute<HttpGetAttribute>()?.Template == "{agentRunId}/boundaries"));
        Assert.IsTrue(methods.Any(method => method.Name == "GetOutputs" && method.GetCustomAttribute<HttpGetAttribute>()?.Template == "{agentRunId}/outputs"));
        Assert.IsTrue(methods.Any(method => method.Name == "GetInputs" && method.GetCustomAttribute<HttpGetAttribute>()?.Template == "{agentRunId}/inputs"));
        Assert.IsFalse(methods.Any(method => method.GetCustomAttribute<HttpPostAttribute>() is not null));
        Assert.IsFalse(methods.Any(method => method.GetCustomAttribute<HttpPutAttribute>() is not null));
        Assert.IsFalse(methods.Any(method => method.GetCustomAttribute<HttpPatchAttribute>() is not null));
        Assert.IsFalse(methods.Any(method => method.GetCustomAttribute<HttpDeleteAttribute>() is not null));

        var routes = methods
            .Select(method => method.GetCustomAttribute<HttpGetAttribute>()?.Template ?? string.Empty)
            .Append(routePrefix)
            .ToArray();
        Assert.IsFalse(routes.Any(route => ContainsAny(route, "run-agent", "approve", "promote", "schedule", "retry")));
    }

    [TestMethod]
    public void AgentRunAuditApi_ControllerReturnsNotFoundForCrossProjectRuns()
    {
        var controller = new AgentRunAuditController(BuildService(BuildEnvelope("1", "agent-run-1", AgentDefinitionCatalog.IndependentCriticAgent)));

        var result = controller.Get(2, "agent-run-1");

        Assert.IsInstanceOfType(result.Result, typeof(NotFoundObjectResult));
    }

    [TestMethod]
    public void AgentRunAuditApi_StaticBoundary_NoControlOrRuntimePaths()
    {
        var repositoryRoot = FindRepositoryRoot();
        var productionFiles = new[]
        {
            Path.Combine(repositoryRoot, "IronDev.Core", "Agents", "Audit", "AgentRunAuditQueryDtos.cs"),
            Path.Combine(repositoryRoot, "IronDev.Core", "Agents", "Audit", "AgentRunAuditQueryService.cs"),
            Path.Combine(repositoryRoot, "IronDev.Core", "Agents", "Audit", "AgentRunAuditDtoMapper.cs"),
            Path.Combine(repositoryRoot, "IronDev.Api", "Controllers", "AgentRunAuditController.cs")
        };
        var forbiddenTokens = new[]
        {
            "HttpPost",
            "HttpPut",
            "HttpPatch",
            "HttpDelete",
            "RunAgent",
            "ExecuteAgent",
            "ApproveAgent",
            "PromoteMemory",
            "ScheduleAgent",
            "RetryAgent",
            "IAgentRuntime",
            "AgentRuntime",
            "AgentScheduler",
            "AgentOrchestrator",
            "AgentPromptRunner",
            "AgentToolRouter",
            "IChatCompletion",
            "OpenAI",
            "Anthropic",
            "Gemini",
            "SqlConnection",
            "CREATE TABLE",
            "CREATE PROCEDURE",
            "Weaviate",
            "INSERT INTO",
            "UPDATE ",
            "DELETE ",
            "MERGE ",
            "AddHostedService",
            "IHostedService",
            "BackgroundService",
            "ICollectiveMemoryPromotionService",
            "SqlCollectiveMemoryPromotionService",
            "IMemoryImprovementProposalStore",
            "SqlMemoryImprovementProposalStore",
            "ManualIndependentCriticAgentService",
            "ManualMemoryImprovementAgentService"
        };

        foreach (var file in productionFiles)
        {
            var text = File.ReadAllText(file);
            foreach (var token in forbiddenTokens)
            {
                Assert.IsFalse(text.Contains(token, StringComparison.Ordinal),
                    $"Agent run audit API production file contains forbidden token '{token}': {file}");
            }
        }
    }

    private static Audit.IAgentRunAuditQueryService BuildService(params Audit.AgentRunAuditEnvelope[] envelopes) =>
        new Audit.AgentRunAuditQueryService(new Audit.InMemoryAgentRunAuditEnvelopeReadRepository(envelopes));

    private static Audit.AgentRunAuditEnvelope BuildEnvelope(
        string projectId,
        string agentRunId,
        AgentDefinition definition,
        Audit.AgentRunStatus status = Audit.AgentRunStatus.CompletedWithWarnings,
        Audit.AgentRunTriggerType triggerType = Audit.AgentRunTriggerType.ManualUserRequest) =>
        new()
        {
            Run = new Audit.AgentRunRecord
            {
                AgentRunId = agentRunId,
                TenantId = "tenant-1",
                ProjectId = projectId,
                CampaignId = "campaign-1",
                RunId = $"run-{agentRunId}",
                AgentId = definition.AgentId,
                AgentName = definition.Name,
                RequestedByUserId = "user-1",
                TriggerType = triggerType,
                Status = status,
                RequestSummary = "Inspect supplied audit evidence.",
                Purpose = "Expose read-only agent run audit evidence.",
                CreatedAtUtc = CreatedAt.AddMinutes(agentRunId.Length),
                StartedAtUtc = CreatedAt.AddMinutes(agentRunId.Length).AddSeconds(1),
                CompletedAtUtc = CreatedAt.AddMinutes(agentRunId.Length).AddSeconds(2)
            },
            AgentDefinitionSnapshot = definition,
            Inputs = [BuildInput(agentRunId)],
            Outputs = [BuildOutput(agentRunId)],
            Steps = [BuildStep(agentRunId)],
            CapabilityUses =
            [
                BuildCapability(agentRunId, AgentCapability.CreateReport, Audit.AgentCapabilityUseOutcome.Allowed, true, false),
                BuildCapability(agentRunId, AgentCapability.RunTool, Audit.AgentCapabilityUseOutcome.Blocked, false, true)
            ],
            BoundaryDecisions = [BuildBoundary(agentRunId)],
            ThoughtLedger = [BuildThought(agentRunId)]
        };

    private static Audit.AgentRunInputRef BuildInput(string agentRunId) =>
        new()
        {
            InputRefId = $"input-{agentRunId}",
            AgentRunId = agentRunId,
            RefType = "AgentRunAuditEnvelope",
            RefId = $"audit-input-{agentRunId}",
            Source = "manual audit fixture",
            Summary = "Safe audit input summary.",
            Sha256 = new string('a', 64)
        };

    private static Audit.AgentRunOutputRef BuildOutput(string agentRunId) =>
        new()
        {
            OutputRefId = $"output-{agentRunId}",
            AgentRunId = agentRunId,
            RefType = "CriticReviewResult",
            RefId = $"critic-result-{agentRunId}",
            Summary = "Safe review-only output summary.",
            Sha256 = new string('b', 64),
            IsReviewOnly = true,
            EvidenceRefs = ["evidence-1"]
        };

    private static Audit.AgentRunStep BuildStep(string agentRunId) =>
        new()
        {
            StepId = $"step-{agentRunId}",
            AgentRunId = agentRunId,
            Sequence = 1,
            StepType = Audit.AgentRunStepType.OutputRecorded,
            OccurredAtUtc = CreatedAt.AddSeconds(1),
            Summary = "Safe output recorded.",
            EvidenceRefs = ["evidence-1"]
        };

    private static Audit.AgentCapabilityUseRecord BuildCapability(
        string agentRunId,
        AgentCapability capability,
        Audit.AgentCapabilityUseOutcome outcome,
        bool declared,
        bool forbidden) =>
        new()
        {
            CapabilityUseId = $"capability-{agentRunId}-{capability}",
            AgentRunId = agentRunId,
            Capability = capability,
            Outcome = outcome,
            Summary = $"{capability} was {outcome}.",
            PolicyDecisionId = "policy-1",
            BoundaryDecisionId = "boundary-1",
            EvidenceRef = "evidence-1",
            WasDeclaredOnAgent = declared,
            WasForbiddenOnAgent = forbidden
        };

    private static Audit.AgentBoundaryDecision BuildBoundary(string agentRunId) =>
        new()
        {
            BoundaryDecisionId = $"boundary-{agentRunId}",
            AgentRunId = agentRunId,
            BoundaryType = Audit.AgentBoundaryDecisionType.Capability,
            Decision = "blocked",
            Reason = "Tool execution remains outside this read-only audit boundary.",
            SourceRefId = "policy-1",
            EvidenceRefs = ["evidence-1"]
        };

    private static Audit.ThoughtLedgerEntry BuildThought(string agentRunId) =>
        new()
        {
            ThoughtLedgerEntryId = $"thought-{agentRunId}",
            AgentRunId = agentRunId,
            EntryType = Audit.ThoughtLedgerEntryType.EvidenceUsed,
            Summary = "Safe rationale summary only.",
            EvidenceRefs = ["evidence-1"],
            RecordedAtUtc = CreatedAt.AddSeconds(2)
        };

    private static void AssertHasIssue(IReadOnlyList<Audit.AgentRunAuditQueryIssueDto> issues, string code)
    {
        Assert.IsTrue(
            issues.Any(issue => string.Equals(issue.Code, code, StringComparison.Ordinal)),
            $"Expected issue '{code}' but got: {string.Join(", ", issues.Select(issue => issue.Code))}");
    }

    private static void AssertNoRawMarkers(string value)
    {
        Assert.IsFalse(ContainsAny(value, "RawPrompt", "RawCompletion", "ChainOfThought", "Scratchpad", "PrivateReasoning"));
    }

    private static bool ContainsAny(string value, params string[] tokens) =>
        tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "IronDev.Core")) &&
                Directory.Exists(Path.Combine(directory.FullName, "IronDev.Api")) &&
                Directory.Exists(Path.Combine(directory.FullName, "IronDev.IntegrationTests")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate AIDeveloper repository root.");
    }
}
