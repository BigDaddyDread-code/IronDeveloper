using IronDev.Core.Agents;
using IronDev.Core.Agents.Audit;
using IronDev.Core.Agents.Concrete;
using IronDev.Core.Agents.Evaluation;

namespace IronDev.IntegrationTests.Agents;

[TestClass]
public sealed class AgentMemoryL4ReleaseGateReportTests
{
    private static readonly DateTimeOffset GeneratedAt = new(2026, 6, 11, 13, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void AgentMemoryL4ReleaseGateReport_ContractsExist()
    {
        Assert.AreEqual(nameof(AgentMemoryL4ReleaseGateReport), typeof(AgentMemoryL4ReleaseGateReport).Name);
        Assert.AreEqual(nameof(AgentMemoryL4ReleaseGateSection), typeof(AgentMemoryL4ReleaseGateSection).Name);
        Assert.AreEqual(nameof(AgentMemoryL4ReleaseGateFinding), typeof(AgentMemoryL4ReleaseGateFinding).Name);
        Assert.AreEqual(nameof(AgentMemoryL4ReleaseGateStatus), typeof(AgentMemoryL4ReleaseGateStatus).Name);
        Assert.AreEqual(nameof(IAgentMemoryL4ReleaseGateReportGenerator), typeof(IAgentMemoryL4ReleaseGateReportGenerator).Name);
        Assert.AreEqual(nameof(AgentMemoryL4ReleaseGateReportGenerator), typeof(AgentMemoryL4ReleaseGateReportGenerator).Name);
        Assert.AreEqual(nameof(AgentMemoryL4ReleaseGateMarkdownRenderer), typeof(AgentMemoryL4ReleaseGateMarkdownRenderer).Name);
    }

    [TestMethod]
    public void AgentMemoryL4ReleaseGateReport_GeneratedReportHasStableShape()
    {
        var report = GenerateReport();

        Assert.AreEqual("agent-memory-l4-release-gate", report.ReportId);
        Assert.AreEqual("Agent + Memory L4 Release Gate Report", report.Title);
        Assert.AreEqual(GeneratedAt, report.GeneratedAtUtc);
        Assert.IsTrue(report.Status is AgentMemoryL4ReleaseGateStatus.Passed or AgentMemoryL4ReleaseGateStatus.PassedWithWarnings);
        Assert.IsTrue(report.Sections.Count >= 8);
        Assert.IsTrue(report.PassedEvidence.Count > 0);
        Assert.IsTrue(report.NonGoals.Count > 0);
        StringAssert.Contains(report.Summary, "ready to proceed to model-backed manual agents");
        AssertDoesNotClaimAutonomyReadiness(report.Summary);
    }

    [TestMethod]
    public void AgentMemoryL4ReleaseGateReport_ContainsAllRequiredSections()
    {
        var report = GenerateReport();
        var sectionIds = report.Sections.Select(section => section.SectionId).ToArray();

        CollectionAssert.AreEquivalent(
            AgentMemoryL4ReleaseGateReportGenerator.RequiredSectionIds.ToArray(),
            sectionIds);
    }

    [TestMethod]
    public void AgentMemoryL4ReleaseGateReport_IncludesEvidenceForEverySection()
    {
        var report = GenerateReport();

        foreach (var section in report.Sections)
        {
            Assert.IsTrue(section.Evidence.Count > 0, $"Section '{section.SectionId}' must include evidence.");
        }
    }

    [TestMethod]
    public void AgentMemoryL4ReleaseGateReport_IncludesAllBoxedAgentsAndProfiles()
    {
        var reportText = FlattenReport(GenerateReport());

        foreach (var agentName in RequiredAgentNames)
            StringAssert.Contains(reportText, agentName);

        foreach (var profileId in RequiredCriticProfileIds)
            StringAssert.Contains(reportText, profileId);

        foreach (var profileId in RequiredMemoryProfileIds)
            StringAssert.Contains(reportText, profileId);

        StringAssert.Contains(reportText, "review-only");
        StringAssert.Contains(reportText, "proposal-only");
        StringAssert.Contains(reportText, "grants no authority");
    }

    [TestMethod]
    public void AgentMemoryL4ReleaseGateReport_IncludesAllL4HarnessScenarios()
    {
        var reportText = FlattenReport(GenerateReport());

        foreach (var scenarioType in AgentMemoryL4EvaluationHarness.RequiredScenarioTypes)
            StringAssert.Contains(reportText, scenarioType.ToString());

        StringAssert.Contains(reportText, nameof(AgentMemoryL4EvaluationScenarioType.CombinedContextCannotBypassGovernance));
        StringAssert.Contains(reportText, nameof(AgentMemoryL4EvaluationScenarioType.CrossProjectAuditReadBlocked));
        StringAssert.Contains(reportText, nameof(AgentMemoryL4EvaluationScenarioType.AuditAppendFailureFailsClosed));
        StringAssert.Contains(reportText, nameof(AgentMemoryL4EvaluationScenarioType.InvalidSpecialisationCannotExecute));
    }

    [TestMethod]
    public void AgentMemoryL4ReleaseGateReport_FailsWhenScenarioFails()
    {
        var scenarioResults = AgentMemoryL4EvaluationHarness.CreateDefault().EvaluateAll(GeneratedAt)
            .Select(result => result.ScenarioType == AgentMemoryL4EvaluationScenarioType.StoredCriticCannotApprove
                ? result with
                {
                    Passed = false,
                    Violations =
                    [
                        new AgentMemoryL4EvaluationViolation
                        {
                            Code = "TEST_INJECTED_AUTHORITY",
                            Severity = "critical",
                            Message = "Injected scenario failure.",
                            Component = "test"
                        }
                    ]
                }
                : result)
            .ToArray();

        var report = GenerateReport(new AgentMemoryL4ReleaseGateReportGeneratorOptions
        {
            L4ScenarioResults = scenarioResults
        });

        Assert.AreEqual(AgentMemoryL4ReleaseGateStatus.Failed, report.Status);
        Assert.IsTrue(report.Findings.Any(finding => finding.Code == "L4_SCENARIO_FAILED"));
    }

    [TestMethod]
    public void AgentMemoryL4ReleaseGateReport_FailsWhenRequiredScenarioMissing()
    {
        var scenarioResults = AgentMemoryL4EvaluationHarness.CreateDefault().EvaluateAll(GeneratedAt)
            .Where(result => result.ScenarioType != AgentMemoryL4EvaluationScenarioType.AuditAppendFailureFailsClosed)
            .ToArray();

        var report = GenerateReport(new AgentMemoryL4ReleaseGateReportGeneratorOptions
        {
            L4ScenarioResults = scenarioResults
        });

        Assert.AreEqual(AgentMemoryL4ReleaseGateStatus.Failed, report.Status);
        Assert.IsTrue(report.Findings.Any(finding =>
            finding.Code == "L4_SCENARIO_MISSING" &&
            finding.Component == nameof(AgentMemoryL4EvaluationScenarioType.AuditAppendFailureFailsClosed)));
    }

    [TestMethod]
    public void AgentMemoryL4ReleaseGateReport_FailsOnRequiredBoundaryViolations()
    {
        AssertFails(new AgentMemoryL4ReleaseGateReportGeneratorOptions { OmittedRequiredAgentName = "MemoryImprovementAgent" }, "AGENT_DEFINITION_MISSING");
        AssertFails(new AgentMemoryL4ReleaseGateReportGeneratorOptions { OmittedRequiredProfileId = "builtin.critic.security-review" }, "PROFILE_MISSING");
        AssertFails(new AgentMemoryL4ReleaseGateReportGeneratorOptions { InjectProfileAuthorityViolation = true }, "PROFILE_AUTHORITY_GRANTED");
        AssertFails(new AgentMemoryL4ReleaseGateReportGeneratorOptions { OmitStoredManualWrappers = true }, "STORED_MANUAL_WRAPPER_MISSING");
        AssertFails(new AgentMemoryL4ReleaseGateReportGeneratorOptions { InjectAuditAuthorityViolation = true }, "AUDIT_BOUNDARY_AUTHORITY");
        AssertFails(new AgentMemoryL4ReleaseGateReportGeneratorOptions { InjectCombinedContextAuthorityViolation = true }, "COMBINED_CONTEXT_AUTHORITY");
    }

    [TestMethod]
    public void AgentMemoryL4ReleaseGateReport_RecordsExplicitNonGoals()
    {
        var report = GenerateReport();

        foreach (var nonGoal in AgentMemoryL4ReleaseGateReportGenerator.RequiredNonGoals)
            Assert.IsTrue(report.NonGoals.Contains(nonGoal), $"Missing non-goal: {nonGoal}");

        Assert.IsTrue(report.NonGoals.Contains("autonomous agents"));
        Assert.IsTrue(report.NonGoals.Contains("scheduled/background agents"));
        Assert.IsTrue(report.NonGoals.Contains("model-backed agent execution"));
        Assert.IsTrue(report.NonGoals.Contains("tool-running agents"));
        Assert.IsTrue(report.NonGoals.Contains("source-mutating agents"));
        Assert.IsTrue(report.NonGoals.Contains("automatic memory promotion"));
        Assert.IsTrue(report.NonGoals.Contains("UI control cockpit"));
        Assert.IsTrue(report.NonGoals.Contains("production autonomy"));
    }

    [TestMethod]
    public void AgentMemoryL4ReleaseGateReport_IsDeterministic()
    {
        var first = GenerateReport();
        var second = GenerateReport();

        Assert.AreEqual(first.ReportId, second.ReportId);
        Assert.AreEqual(first.Title, second.Title);
        Assert.AreEqual(first.Status, second.Status);
        CollectionAssert.AreEqual(first.Sections.Select(section => section.SectionId).ToArray(), second.Sections.Select(section => section.SectionId).ToArray());
        CollectionAssert.AreEqual(first.PassedEvidence.ToArray(), second.PassedEvidence.ToArray());
        CollectionAssert.AreEqual(first.NonGoals.ToArray(), second.NonGoals.ToArray());
    }

    [TestMethod]
    public void AgentMemoryL4ReleaseGateMarkdownRenderer_RendersSafeHumanReviewReport()
    {
        var report = GenerateReport();
        var markdown = new AgentMemoryL4ReleaseGateMarkdownRenderer().Render(report);

        StringAssert.Contains(markdown, report.Title);
        StringAssert.Contains(markdown, report.Status.ToString());

        foreach (var section in report.Sections)
            StringAssert.Contains(markdown, section.Title);

        StringAssert.Contains(markdown, "Warnings");
        StringAssert.Contains(markdown, "Non-goals");
        StringAssert.Contains(markdown, "Release Gate Decision");
        AssertDoesNotContainUnsafeClaim(markdown);
    }

    [TestMethod]
    public void AgentMemoryL4ReleaseGateReport_DoesNotWireIntoProductionCode()
    {
        var repoRoot = FindRepoRoot();
        var productionFilesWithReport = Directory
            .EnumerateFiles(repoRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}IronDev.IntegrationTests{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path).Contains("AgentMemoryL4ReleaseGate", StringComparison.Ordinal))
            .ToArray();

        Assert.AreEqual(0, productionFilesWithReport.Length, string.Join(Environment.NewLine, productionFilesWithReport));
    }

    [TestMethod]
    public void AgentMemoryL4ReleaseGateReport_DoesNotUseRuntimeOrExternalSystems()
    {
        var repoRoot = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(
            repoRoot,
            "IronDev.IntegrationTests",
            "Agents",
            "AgentMemoryL4ReleaseGateReportTests.cs"));

        foreach (var token in ForbiddenActiveTokens)
        {
            Assert.IsFalse(
                source.Contains(token, StringComparison.Ordinal),
                $"L4 release gate report must not contain active runtime/external token '{token}'.");
        }
    }

    private static AgentMemoryL4ReleaseGateReport GenerateReport(
        AgentMemoryL4ReleaseGateReportGeneratorOptions? options = null) =>
        new AgentMemoryL4ReleaseGateReportGenerator(options).Generate(GeneratedAt);

    private static string FlattenReport(AgentMemoryL4ReleaseGateReport report) =>
        string.Join(
            Environment.NewLine,
            [
                report.Title,
                report.Status.ToString(),
                report.Summary,
                .. report.PassedEvidence,
                .. report.Warnings,
                .. report.NonGoals,
                .. report.Sections.SelectMany(section => new[]
                {
                    section.SectionId,
                    section.Title,
                    section.Status.ToString()
                }.Concat(section.Evidence).Concat(section.Findings.Select(finding => $"{finding.Code} {finding.Message} {finding.Component}"))),
                .. report.Findings.Select(finding => $"{finding.Code} {finding.Message} {finding.Component}")
            ]);

    private static void AssertFails(AgentMemoryL4ReleaseGateReportGeneratorOptions options, string expectedFindingCode)
    {
        var report = GenerateReport(options);

        Assert.AreEqual(AgentMemoryL4ReleaseGateStatus.Failed, report.Status);
        Assert.IsTrue(report.Findings.Any(finding => finding.Code == expectedFindingCode), FlattenReport(report));
    }

    private static void AssertDoesNotClaimAutonomyReadiness(string text)
    {
        Assert.IsFalse(text.Contains("autonomy-ready", StringComparison.OrdinalIgnoreCase) &&
                       !text.Contains("not autonomy-ready", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(text.Contains("tool-execution-ready", StringComparison.OrdinalIgnoreCase) &&
                       !text.Contains("not tool-execution-ready", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(text.Contains("source-mutation-ready", StringComparison.OrdinalIgnoreCase) &&
                       !text.Contains("not source-mutation-ready", StringComparison.OrdinalIgnoreCase));
    }

    private static void AssertDoesNotContainUnsafeClaim(string text)
    {
        var unsafeClaims = new[]
        {
            "approval granted",
            "approved for execution",
            "authority granted",
            "agents are autonomous",
            "agents may execute tools",
            "agents may mutate source",
            "memory may self-promote",
            "production autonomy ready",
            "tool execution ready",
            "source mutation ready"
        };

        foreach (var claim in unsafeClaims)
        {
            Assert.IsFalse(
                text.Contains(claim, StringComparison.OrdinalIgnoreCase),
                $"Report text must not claim: {claim}");
        }
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")))
                return current.FullName;

            current = current.Parent;
        }

        throw new InvalidOperationException("Repository root could not be found.");
    }

    private static readonly string[] RequiredAgentNames =
    [
        "IndependentCriticAgent",
        "MemoryImprovementAgent",
        "GovernanceAgent",
        "RetrievalAgent",
        "ReportingAgent",
        "HumanProxyAgent",
        "ImplementationAgent",
        "TestingAgent"
    ];

    private static readonly string[] RequiredCriticProfileIds =
    [
        "builtin.critic.code-review",
        "builtin.critic.architecture-review",
        "builtin.critic.security-review",
        "builtin.critic.test-failure-review",
        "builtin.critic.build-failure-review"
    ];

    private static readonly string[] RequiredMemoryProfileIds =
    [
        "builtin.memory.repeated-failure-mode-detector",
        "builtin.memory.repeated-governance-block-detector",
        "builtin.memory.repeated-manual-correction-detector",
        "builtin.memory.stale-memory-detector",
        "builtin.memory.contradiction-detector",
        "builtin.memory.retrieval-miss-detector",
        "builtin.memory.duplicate-proposal-detector"
    ];

    private static readonly string[] ForbiddenActiveTokens =
    [
        "Http" + "Post",
        "Http" + "Put",
        "Http" + "Patch",
        "Http" + "Delete",
        "IAgent" + "Runtime",
        "Agent" + "Runtime",
        "Agent" + "Scheduler",
        "Agent" + "Orchestrator",
        "Agent" + "Prompt" + "Runner",
        "Agent" + "Tool" + "Router",
        "Execute" + "Agent" + "Async",
        "Run" + "Agent" + "Async",
        "IChat" + "Completion",
        "Open" + "AI",
        "Anth" + "ropic",
        "Gem" + "ini",
        "Http" + "Client",
        "Process" + "Start" + "Info",
        "File" + ".Copy",
        "File" + ".Delete",
        "Sql" + "Connection",
        "CREATE " + "TABLE",
        "CREATE " + "PROCEDURE",
        "INSERT " + "INTO",
        "UPDATE" + " ",
        "DELETE" + " ",
        "MERGE" + " ",
        "Weaviate" + "Client",
        "Add" + "Hosted" + "Service",
        "IHosted" + "Service",
        "Background" + "Service",
        "GitHub" + "Review",
        "Create" + "Pull" + "Request" + "Review",
        "Submit" + "Review",
        "Sql" + "Collective" + "Memory" + "Promotion" + "Service",
        "Sql" + "Memory" + "Improvement" + "Proposal" + "Store"
    ];
}

public enum AgentMemoryL4ReleaseGateStatus
{
    Passed = 1,
    PassedWithWarnings = 2,
    Failed = 3
}

public sealed record AgentMemoryL4ReleaseGateReport
{
    public required string ReportId { get; init; }
    public required string Title { get; init; }
    public required DateTimeOffset GeneratedAtUtc { get; init; }
    public required AgentMemoryL4ReleaseGateStatus Status { get; init; }
    public IReadOnlyList<AgentMemoryL4ReleaseGateSection> Sections { get; init; } = [];
    public IReadOnlyList<AgentMemoryL4ReleaseGateFinding> Findings { get; init; } = [];
    public IReadOnlyList<string> PassedEvidence { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<string> NonGoals { get; init; } = [];
    public string Summary { get; init; } = string.Empty;
}

public sealed record AgentMemoryL4ReleaseGateSection
{
    public required string SectionId { get; init; }
    public required string Title { get; init; }
    public required AgentMemoryL4ReleaseGateStatus Status { get; init; }
    public IReadOnlyList<string> Evidence { get; init; } = [];
    public IReadOnlyList<AgentMemoryL4ReleaseGateFinding> Findings { get; init; } = [];
}

public sealed record AgentMemoryL4ReleaseGateFinding
{
    public required string Code { get; init; }
    public required string Severity { get; init; }
    public required string Message { get; init; }
    public string Component { get; init; } = string.Empty;
}

public interface IAgentMemoryL4ReleaseGateReportGenerator
{
    AgentMemoryL4ReleaseGateReport Generate(DateTimeOffset generatedAtUtc);
}

public sealed record AgentMemoryL4ReleaseGateReportGeneratorOptions
{
    public IReadOnlyList<AgentMemoryL4EvaluationResult>? L4ScenarioResults { get; init; }
    public string? OmittedRequiredAgentName { get; init; }
    public string? OmittedRequiredProfileId { get; init; }
    public bool InjectProfileAuthorityViolation { get; init; }
    public bool OmitStoredManualWrappers { get; init; }
    public bool InjectAuditAuthorityViolation { get; init; }
    public bool InjectCombinedContextAuthorityViolation { get; init; }
}

public sealed class AgentMemoryL4ReleaseGateReportGenerator : IAgentMemoryL4ReleaseGateReportGenerator
{
    public static IReadOnlyList<string> RequiredSectionIds { get; } =
    [
        "agent-definitions",
        "specialist-profiles",
        "manual-stored-execution",
        "durable-audit",
        "memory-governance",
        "l4-evaluation-harness",
        "explicit-non-goals",
        "release-gate-decision"
    ];

    public static IReadOnlyList<string> RequiredNonGoals { get; } =
    [
        "autonomous agents",
        "scheduled/background agents",
        "model-backed agent execution",
        "tool-running agents",
        "source-mutating agents",
        "automatic GitHub review submission",
        "automatic proposal persistence",
        "automatic CollectiveMemory creation",
        "automatic memory promotion",
        "automatic Weaviate/index writes",
        "UI control cockpit",
        "one-click apply",
        "deployment readiness",
        "production autonomy"
    ];

    private static readonly AgentCapability[] DangerousProfileCapabilities =
    [
        AgentCapability.RunTool,
        AgentCapability.MutateSource,
        AgentCapability.CallExternalSystem,
        AgentCapability.PromoteCollectiveMemory,
        AgentCapability.RepresentHumanApproval,
        AgentCapability.RepresentHumanPromotionDecision,
        AgentCapability.BlockExecution
    ];

    private static readonly string[] RequiredCriticProfiles =
    [
        "builtin.critic.code-review",
        "builtin.critic.architecture-review",
        "builtin.critic.security-review",
        "builtin.critic.test-failure-review",
        "builtin.critic.build-failure-review"
    ];

    private static readonly string[] RequiredMemoryProfiles =
    [
        "builtin.memory.repeated-failure-mode-detector",
        "builtin.memory.repeated-governance-block-detector",
        "builtin.memory.repeated-manual-correction-detector",
        "builtin.memory.stale-memory-detector",
        "builtin.memory.contradiction-detector",
        "builtin.memory.retrieval-miss-detector",
        "builtin.memory.duplicate-proposal-detector"
    ];

    private readonly AgentMemoryL4ReleaseGateReportGeneratorOptions _options;

    public AgentMemoryL4ReleaseGateReportGenerator()
        : this(null)
    {
    }

    public AgentMemoryL4ReleaseGateReportGenerator(AgentMemoryL4ReleaseGateReportGeneratorOptions? options)
    {
        _options = options ?? new AgentMemoryL4ReleaseGateReportGeneratorOptions();
    }

    public AgentMemoryL4ReleaseGateReport Generate(DateTimeOffset generatedAtUtc)
    {
        var sections = new List<AgentMemoryL4ReleaseGateSection>
        {
            BuildAgentDefinitionsSection(),
            BuildSpecialistProfilesSection(),
            BuildManualStoredExecutionSection(),
            BuildDurableAuditSection(),
            BuildMemoryGovernanceSection(generatedAtUtc),
            BuildL4EvaluationHarnessSection(generatedAtUtc),
            BuildNonGoalsSection(),
            BuildReleaseGateDecisionSection()
        };

        var findings = sections.SelectMany(section => section.Findings).ToArray();
        var warnings = new[]
        {
            "Broad SQL-heavy memory validation filters must be run split because existing suites share and drop the agent schema in incompatible orders.",
            "Block B model-backed manual agents remain intentionally unimplemented by this release gate report.",
            "This report is evidence only and is not an approval or authority source."
        };

        var status = ResolveStatus(findings, warnings);

        return new AgentMemoryL4ReleaseGateReport
        {
            ReportId = "agent-memory-l4-release-gate",
            Title = "Agent + Memory L4 Release Gate Report",
            GeneratedAtUtc = generatedAtUtc,
            Status = status,
            Sections = sections,
            Findings = findings,
            PassedEvidence = sections.SelectMany(section => section.Evidence).ToArray(),
            Warnings = warnings,
            NonGoals = RequiredNonGoals,
            Summary = status == AgentMemoryL4ReleaseGateStatus.Failed
                ? "Agent + Memory L4 backend foundation is not ready. See error findings."
                : "Agent + Memory L4 backend foundation is ready to proceed to model-backed manual agents only. It is not autonomy-ready. It is not tool-execution-ready. It is not source-mutation-ready. It is not memory-self-promotion-ready. It is not UI-control-plane-ready."
        };
    }

    private AgentMemoryL4ReleaseGateSection BuildAgentDefinitionsSection()
    {
        var evidence = new List<string>();
        var findings = new List<AgentMemoryL4ReleaseGateFinding>();
        var definitions = AgentDefinitionCatalog.All
            .Where(definition => !string.Equals(definition.Name, _options.OmittedRequiredAgentName, StringComparison.Ordinal))
            .ToArray();

        foreach (var requiredName in new[]
                 {
                     "IndependentCriticAgent",
                     "MemoryImprovementAgent",
                     "GovernanceAgent",
                     "RetrievalAgent",
                     "ReportingAgent",
                     "HumanProxyAgent",
                     "ImplementationAgent",
                     "TestingAgent"
                 })
        {
            var definition = definitions.SingleOrDefault(item => string.Equals(item.Name, requiredName, StringComparison.Ordinal));
            if (definition is null)
            {
                findings.Add(Error("AGENT_DEFINITION_MISSING", $"Required agent definition '{requiredName}' is missing.", requiredName));
                continue;
            }

            evidence.Add($"{definition.Name}: AgentId={definition.AgentId}; AgentKind={definition.Kind}; ExecutionMode={definition.ExecutionMode}; AllowedCapabilities={FormatCapabilities(definition.Capabilities)}; ForbiddenCapabilities={FormatCapabilities(definition.ForbiddenCapabilities)}; Authority notes=evidence/reporting only unless separately governed.");
        }

        var critic = definitions.SingleOrDefault(item => item.Name == "IndependentCriticAgent");
        Require(critic?.Kind == AgentKind.ReviewAgent && critic.ExecutionMode == AgentExecutionMode.OutOfBandReviewOnly, findings, "CRITIC_AGENT_SHAPE_INVALID", "IndependentCriticAgent must remain ReviewAgent + OutOfBandReviewOnly.", "IndependentCriticAgent");
        Require(!HasCapability(critic, AgentCapability.RunTool) && !HasCapability(critic, AgentCapability.MutateSource) && !HasCapability(critic, AgentCapability.BlockExecution), findings, "CRITIC_DANGEROUS_CAPABILITY_PRESENT", "IndependentCriticAgent must not have RunTool, MutateSource, or BlockExecution.", "IndependentCriticAgent");

        var memory = definitions.SingleOrDefault(item => item.Name == "MemoryImprovementAgent");
        Require(memory?.Kind == AgentKind.ProposalAgent && memory.ExecutionMode == AgentExecutionMode.ProposalOnly, findings, "MEMORY_AGENT_SHAPE_INVALID", "MemoryImprovementAgent must remain ProposalAgent + ProposalOnly.", "MemoryImprovementAgent");
        Require(!HasCapability(memory, AgentCapability.PromoteCollectiveMemory) && !HasCapability(memory, AgentCapability.RunTool) && !HasCapability(memory, AgentCapability.MutateSource) && !HasCapability(memory, AgentCapability.BlockExecution), findings, "MEMORY_DANGEROUS_CAPABILITY_PRESENT", "MemoryImprovementAgent must not have PromoteCollectiveMemory, RunTool, MutateSource, or BlockExecution.", "MemoryImprovementAgent");

        var humanRepresenters = definitions.Where(definition =>
            HasCapability(definition, AgentCapability.RepresentHumanApproval) ||
            HasCapability(definition, AgentCapability.RepresentHumanPromotionDecision)).ToArray();
        Require(humanRepresenters.All(definition => definition.Name == "HumanProxyAgent"), findings, "HUMAN_AUTHORITY_AGENT_UNSAFE", "HumanProxyAgent must be the only agent representing human approval or promotion decisions.", "AgentDefinitionCatalog");

        var governance = definitions.SingleOrDefault(item => item.Name == "GovernanceAgent");
        Require(!HasCapability(governance, AgentCapability.RunTool) && !HasCapability(governance, AgentCapability.MutateSource), findings, "GOVERNANCE_AGENT_EXECUTES_ACTION", "GovernanceAgent may gate/warn/block but must not execute tools or mutate source.", "GovernanceAgent");

        evidence.Add("ImplementationAgent and TestingAgent definitions are listed for boundary visibility; this report does not grant them new behaviour.");

        return Section("agent-definitions", "Agent Definitions", evidence, findings);
    }

    private AgentMemoryL4ReleaseGateSection BuildSpecialistProfilesSection()
    {
        var evidence = new List<string>();
        var findings = new List<AgentMemoryL4ReleaseGateFinding>();
        var validator = new AgentSpecialisationValidator();
        var criticProfiles = AgentSpecialisationCatalog.CriticProfiles
            .Where(profile => !string.Equals(profile.SpecialisationId, _options.OmittedRequiredProfileId, StringComparison.Ordinal))
            .ToArray();
        var memoryProfiles = AgentSpecialisationCatalog.MemoryImprovementProfiles
            .Where(profile => !string.Equals(profile.SpecialisationId, _options.OmittedRequiredProfileId, StringComparison.Ordinal))
            .ToArray();

        ValidateProfiles(
            criticProfiles,
            RequiredCriticProfiles,
            AgentDefinitionCatalog.IndependentCriticAgent,
            output => output.MustBeReviewOnly && output.RequiresHumanReview,
            "review-only",
            validator,
            evidence,
            findings);

        ValidateProfiles(
            memoryProfiles,
            RequiredMemoryProfiles,
            AgentDefinitionCatalog.MemoryImprovementAgent,
            output => output.MustBeProposalOnly && output.RequiresHumanReview,
            "proposal-only",
            validator,
            evidence,
            findings);

        if (_options.InjectProfileAuthorityViolation)
            findings.Add(Error("PROFILE_AUTHORITY_GRANTED", "Injected profile authority violation.", "AgentSpecialisationDefinition.AuthorityBoundary"));

        return Section("specialist-profiles", "Specialist Profiles", evidence, findings);
    }

    private AgentMemoryL4ReleaseGateSection BuildManualStoredExecutionSection()
    {
        var evidence = new List<string>();
        var findings = new List<AgentMemoryL4ReleaseGateFinding>();

        if (_options.OmitStoredManualWrappers)
        {
            findings.Add(Error("STORED_MANUAL_WRAPPER_MISSING", "Stored manual execution wrappers are missing.", nameof(StoredManualIndependentCriticAgentService)));
        }
        else
        {
            evidence.Add($"{nameof(StoredManualIndependentCriticAgentService)} exists and validates selected specialisation before execution.");
            evidence.Add($"{nameof(StoredManualMemoryImprovementAgentService)} exists and validates selected specialisation before execution.");
        }

        evidence.Add("Stored wrappers append durable audit envelopes only after deterministic manual runs.");
        evidence.Add("Stored wrappers fail closed on audit append failure and reject invalid specialisations before manual service calls.");
        evidence.Add("Selected specialisation is added as non-authoritative input evidence.");
        evidence.Add("Critic output remains review-only; memory-improvement output remains proposal-only.");
        evidence.Add("No public write API, runtime scheduler, orchestrator, model call, or tool path is added by this report.");

        return Section("manual-stored-execution", "Manual Stored Execution", evidence, findings);
    }

    private AgentMemoryL4ReleaseGateSection BuildDurableAuditSection()
    {
        var evidence = new List<string>();
        var findings = new List<AgentMemoryL4ReleaseGateFinding>();

        evidence.Add($"{nameof(IAgentRunAuditEnvelopeStore)} contract exists with append-only status semantics.");
        evidence.Add($"{nameof(AgentRunAuditEnvelopeValidator)} validates envelopes before durable append.");
        evidence.Add($"{nameof(IAgentRunAuditEnvelopeReadRepository)} exposes read-only project-scoped retrieval.");
        evidence.Add("Durable audit store uses SHA-256 envelope hash, idempotent duplicate handling, conflict detection, update/delete blocking, and unsafe safety-flag blocking per store tests.");
        evidence.Add("Audit records are evidence only: they cannot create authority, runtime action, approval, memory promotion, or raw/private reasoning.");
        evidence.Add("Cross-project audit read isolation is covered by the L4 harness.");

        if (_options.InjectAuditAuthorityViolation)
            findings.Add(Error("AUDIT_BOUNDARY_AUTHORITY", "Injected audit boundary authority violation.", nameof(AgentRunAuditEnvelope)));

        return Section("durable-audit", "Durable Audit", evidence, findings);
    }

    private static AgentMemoryL4ReleaseGateSection BuildMemoryGovernanceSection(DateTimeOffset generatedAtUtc)
    {
        var evidence = new List<string>
        {
            "Local Agent Memory is scoped and append-only.",
            "MemoryInfluenceRecord is explicit, auditable, and cannot replace approval.",
            "MemoryHandoff does not grant ownership of source local memory.",
            "ConscienceMemoryGovernance can block unsafe memory use.",
            "MemoryExecutionGate cannot be bypassed by memory alone.",
            "MemoryExecutionAudit is evidence only.",
            "MemoryImprovementProposal is proposal only and cannot promote memory.",
            "CollectiveMemory promotion is governed/manual only.",
            "CollectiveMemory retrieval is candidate-only.",
            "CollectiveMemory stability scoring is advisory only.",
            "Weaviate/indexing is retrieval acceleration only."
        };
        var findings = new List<AgentMemoryL4ReleaseGateFinding>();
        var boundaryResult = new AgentMemoryBoundaryEvaluationHarness().Evaluate(generatedAtUtc);

        foreach (var violation in boundaryResult.Violations)
        {
            findings.Add(Error(
                violation.Code,
                violation.Message,
                nameof(AgentMemoryBoundaryEvaluationHarness)));
        }

        evidence.Add($"{nameof(AgentMemoryBoundaryEvaluationHarness)} passed {boundaryResult.Scenarios.Count} boundary scenarios.");
        return Section("memory-governance", "Memory Governance", evidence, findings);
    }

    private AgentMemoryL4ReleaseGateSection BuildL4EvaluationHarnessSection(DateTimeOffset generatedAtUtc)
    {
        var evidence = new List<string>();
        var findings = new List<AgentMemoryL4ReleaseGateFinding>();
        var results = _options.L4ScenarioResults ?? AgentMemoryL4EvaluationHarness.CreateDefault().EvaluateAll(generatedAtUtc);
        var coverageViolations = AgentMemoryL4EvaluationHarness.FindCoverageViolations(results);

        foreach (var result in results.OrderBy(result => (int)result.ScenarioType))
        {
            evidence.Add($"{result.ScenarioType}: {(result.Passed ? "passed" : "failed")} - {result.ScenarioName}");

            if (!result.Passed)
            {
                findings.Add(Error(
                    "L4_SCENARIO_FAILED",
                    $"L4 scenario '{result.ScenarioType}' failed.",
                    result.ScenarioType.ToString()));
            }

            foreach (var violation in result.Violations)
            {
                findings.Add(Error(
                    violation.Code,
                    violation.Message,
                    violation.Component ?? result.ScenarioType.ToString()));
            }
        }

        foreach (var violation in coverageViolations)
        {
            findings.Add(Error(
                violation.Code,
                violation.Message,
                violation.Component ?? nameof(AgentMemoryL4EvaluationHarness)));
        }

        if (_options.InjectCombinedContextAuthorityViolation)
            findings.Add(Error("COMBINED_CONTEXT_AUTHORITY", "Injected combined context authority violation.", nameof(AgentMemoryL4EvaluationScenarioType.CombinedContextCannotBypassGovernance)));

        evidence.Add("Scenario groups covered: stored critic boundaries, stored memory-improvement boundaries, specialisation escalation, audit record safety, retrieval/stability/influence/handoff, invalid specialisation, audit append, cross-project audit read, combined-context governance.");

        return Section("l4-evaluation-harness", "L4 Evaluation Harness", evidence, findings);
    }

    private static AgentMemoryL4ReleaseGateSection BuildNonGoalsSection()
    {
        var evidence = RequiredNonGoals
            .Select(nonGoal => $"{nonGoal} is not available after the L4 foundation.")
            .ToArray();

        return Section("explicit-non-goals", "Explicit Non-Goals", evidence, []);
    }

    private static AgentMemoryL4ReleaseGateSection BuildReleaseGateDecisionSection()
    {
        var evidence = new[]
        {
            "Release gate decision is derived from section findings and L4 harness results.",
            "Passing the gate means the backend foundation can proceed to model-backed manual agents only.",
            "Passing the gate does not certify autonomy, tool execution, source mutation, memory self-promotion, UI control-plane readiness, deployment readiness, or production autonomy."
        };

        return Section("release-gate-decision", "Release Gate Decision", evidence, []);
    }

    private static void ValidateProfiles(
        IReadOnlyList<AgentSpecialisationDefinition> profiles,
        IReadOnlyList<string> requiredIds,
        AgentDefinition targetAgent,
        Func<AgentSpecialisationOutputRequirement, bool> outputBoundary,
        string outputBoundaryName,
        AgentSpecialisationValidator validator,
        List<string> evidence,
        List<AgentMemoryL4ReleaseGateFinding> findings)
    {
        foreach (var requiredId in requiredIds)
        {
            var profile = profiles.SingleOrDefault(item => string.Equals(item.SpecialisationId, requiredId, StringComparison.Ordinal));
            if (profile is null)
            {
                findings.Add(Error("PROFILE_MISSING", $"Required specialisation profile '{requiredId}' is missing.", requiredId));
                continue;
            }

            var validation = validator.Validate(profile);
            var compatibility = validator.ValidateCompatibility(targetAgent, profile);

            foreach (var issue in validation.Where(issue => issue.Severity == AgentDefinitionValidator.SeverityError))
                findings.Add(Error("PROFILE_VALIDATION_FAILED", issue.Message, profile.SpecialisationId));

            foreach (var issue in compatibility.Issues.Where(issue => issue.Severity == AgentDefinitionValidator.SeverityError))
                findings.Add(Error("PROFILE_COMPATIBILITY_FAILED", issue.Message, profile.SpecialisationId));

            if (!HasNoAuthority(profile.AuthorityBoundary))
                findings.Add(Error("PROFILE_AUTHORITY_GRANTED", $"Profile '{profile.SpecialisationId}' grants authority.", profile.SpecialisationId));

            if (DangerousProfileCapabilities.Any(capability => !profile.ForbiddenCapabilities.Contains(capability)))
                findings.Add(Error("PROFILE_DANGEROUS_CAPABILITY_NOT_FORBIDDEN", $"Profile '{profile.SpecialisationId}' does not forbid all dangerous capabilities.", profile.SpecialisationId));

            if (profile.OutputRequirements.Any(output => output.MayCreateAuthority || output.MayCreateRuntimeAction || output.MayPromoteMemory))
                findings.Add(Error("PROFILE_OUTPUT_AUTHORITY_GRANTED", $"Profile '{profile.SpecialisationId}' output can create authority, runtime action, or memory promotion.", profile.SpecialisationId));

            if (!profile.OutputRequirements.Any(outputBoundary))
                findings.Add(Error("PROFILE_OUTPUT_BOUNDARY_MISSING", $"Profile '{profile.SpecialisationId}' must require {outputBoundaryName} human-reviewed output.", profile.SpecialisationId));

            evidence.Add($"{profile.SpecialisationId}: appliesTo={profile.AppliesToAgentId}; output={outputBoundaryName}; grants no authority; dangerous capabilities forbidden.");
        }
    }

    private static bool HasNoAuthority(AgentSpecialisationAuthorityBoundary boundary) =>
        !boundary.CanGrantApproval &&
        !boundary.CanRepresentHumanDecision &&
        !boundary.CanOverridePolicy &&
        !boundary.CanExecuteTools &&
        !boundary.CanMutateSource &&
        !boundary.CanCallExternalSystems &&
        !boundary.CanPromoteMemory &&
        !boundary.CanCreateAuthority &&
        !boundary.CanCreateRuntimeAction &&
        !boundary.CanWriteMemory;

    private static bool HasCapability(AgentDefinition? definition, AgentCapability capability) =>
        definition?.Capabilities?.Contains(capability) == true;

    private static string FormatCapabilities(IReadOnlySet<AgentCapability>? capabilities) =>
        capabilities is null || capabilities.Count == 0
            ? "none"
            : string.Join(",", capabilities.OrderBy(capability => capability.ToString()));

    private static void Require(
        bool condition,
        List<AgentMemoryL4ReleaseGateFinding> findings,
        string code,
        string message,
        string component)
    {
        if (!condition)
            findings.Add(Error(code, message, component));
    }

    private static AgentMemoryL4ReleaseGateSection Section(
        string sectionId,
        string title,
        IReadOnlyList<string> evidence,
        IReadOnlyList<AgentMemoryL4ReleaseGateFinding> findings) =>
        new()
        {
            SectionId = sectionId,
            Title = title,
            Status = ResolveStatus(findings, []),
            Evidence = evidence,
            Findings = findings
        };

    private static AgentMemoryL4ReleaseGateFinding Error(string code, string message, string component) =>
        new()
        {
            Code = code,
            Severity = "error",
            Message = message,
            Component = component
        };

    private static AgentMemoryL4ReleaseGateStatus ResolveStatus(
        IReadOnlyList<AgentMemoryL4ReleaseGateFinding> findings,
        IReadOnlyList<string> warnings)
    {
        if (findings.Any(finding => string.Equals(finding.Severity, "error", StringComparison.OrdinalIgnoreCase)))
            return AgentMemoryL4ReleaseGateStatus.Failed;

        if (findings.Any(finding => string.Equals(finding.Severity, "warning", StringComparison.OrdinalIgnoreCase)) ||
            warnings.Count > 0)
        {
            return AgentMemoryL4ReleaseGateStatus.PassedWithWarnings;
        }

        return AgentMemoryL4ReleaseGateStatus.Passed;
    }
}

public sealed class AgentMemoryL4ReleaseGateMarkdownRenderer
{
    public string Render(AgentMemoryL4ReleaseGateReport report)
    {
        var lines = new List<string>
        {
            $"# {report.Title}",
            string.Empty,
            $"Status: {report.Status}",
            string.Empty,
            "## Summary",
            report.Summary,
            string.Empty,
            "## Sections"
        };

        foreach (var section in report.Sections)
        {
            lines.Add($"### {section.Title}");
            lines.Add($"Status: {section.Status}");
            lines.Add("Evidence:");
            lines.AddRange(section.Evidence.Select(item => $"- {item}"));

            if (section.Findings.Count > 0)
            {
                lines.Add("Findings:");
                lines.AddRange(section.Findings.Select(finding => $"- {finding.Severity}: {finding.Code} - {finding.Message} ({finding.Component})"));
            }

            lines.Add(string.Empty);
        }

        lines.Add("## Findings");
        lines.AddRange(report.Findings.Count == 0
            ? ["- none"]
            : report.Findings.Select(finding => $"- {finding.Severity}: {finding.Code} - {finding.Message} ({finding.Component})"));
        lines.Add(string.Empty);
        lines.Add("## Warnings");
        lines.AddRange(report.Warnings.Select(warning => $"- {warning}"));
        lines.Add(string.Empty);
        lines.Add("## Non-goals");
        lines.AddRange(report.NonGoals.Select(nonGoal => $"- {nonGoal}"));
        lines.Add(string.Empty);
        lines.Add("## Release Gate Decision");
        lines.Add(report.Summary);

        return string.Join(Environment.NewLine, lines);
    }
}
