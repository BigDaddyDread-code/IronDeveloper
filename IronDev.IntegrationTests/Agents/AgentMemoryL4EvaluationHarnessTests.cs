using IronDev.Core.Agents;
using IronDev.Core.Agents.Audit;
using IronDev.Core.Agents.Concrete;
using IronDev.Core.Agents.Evaluation;

namespace IronDev.IntegrationTests.Agents;

[TestClass]
public sealed class AgentMemoryL4EvaluationHarnessTests
{
    private static readonly DateTimeOffset EvaluatedAt = new(2026, 6, 11, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void AgentMemoryL4EvaluationHarness_ContainsAllRequiredScenarios()
    {
        var required = Enum.GetValues<AgentMemoryL4EvaluationScenarioType>();
        var defined = AgentMemoryL4EvaluationHarness.RequiredScenarioTypes;

        CollectionAssert.AreEquivalent(required, defined.ToArray());
        Assert.AreEqual(23, defined.Count);
    }

    [TestMethod]
    public void AgentMemoryL4EvaluationHarness_AllScenariosPass()
    {
        var results = AgentMemoryL4EvaluationHarness.CreateDefault().EvaluateAll(EvaluatedAt);
        var coverageViolations = AgentMemoryL4EvaluationHarness.FindCoverageViolations(results);

        Assert.AreEqual(0, coverageViolations.Count, FormatViolations(coverageViolations));
        Assert.IsTrue(results.All(result => result.Passed), FormatResults(results));
        Assert.IsTrue(results.All(result => result.Evidence.Count > 0), "Every L4 scenario must include evidence.");
    }

    [TestMethod]
    public void AgentMemoryL4EvaluationHarness_FailsIfScenarioMissing()
    {
        var results = AgentMemoryL4EvaluationHarness.CreateDefault()
            .EvaluateAll(EvaluatedAt)
            .Where(result => result.ScenarioType != AgentMemoryL4EvaluationScenarioType.StoredCriticCannotApprove)
            .ToArray();

        var coverageViolations = AgentMemoryL4EvaluationHarness.FindCoverageViolations(results);

        Assert.IsTrue(coverageViolations.Any(violation =>
            violation.Code == "L4_SCENARIO_MISSING" &&
            violation.Component == nameof(AgentMemoryL4EvaluationScenarioType.StoredCriticCannotApprove)));
    }

    [TestMethod]
    public void AgentMemoryL4EvaluationHarness_FailsOnAuthorityViolation()
    {
        var result = AgentMemoryL4EvaluationHarness.EvaluateAuthorityFacts(
            AgentMemoryL4EvaluationScenarioType.AuditRecordCannotGrantApproval,
            "Injected authority fact should fail.",
            [new AgentMemoryL4AuthorityFact("audit.boundary", "GrantsHumanApproval", true)],
            ["Synthetic authority fact injected by regression test."]);

        Assert.IsFalse(result.Passed);
        Assert.IsTrue(result.Violations.Any(violation => violation.Code == "L4_AUTHORITY_GRANTED"));
    }

    [TestMethod]
    public void AgentMemoryL4EvaluationHarness_DoesNotUseRuntimeOrExternalSystems()
    {
        var repoRoot = FindRepoRoot();
        var harnessPath = Path.Combine(
            repoRoot,
            "IronDev.IntegrationTests",
            "Agents",
            "AgentMemoryL4EvaluationHarnessTests.cs");
        var source = File.ReadAllText(harnessPath);

        foreach (var token in ForbiddenRuntimeTokens)
        {
            Assert.IsFalse(
                source.Contains(token, StringComparison.Ordinal),
                $"L4 evaluation harness must not contain runtime/external token '{token}'.");
        }

        var productionFilesWithHarness = Directory
            .EnumerateFiles(repoRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}IronDev.IntegrationTests{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path).Contains("AgentMemoryL4Evaluation", StringComparison.Ordinal))
            .ToArray();

        Assert.AreEqual(0, productionFilesWithHarness.Length, string.Join(Environment.NewLine, productionFilesWithHarness));
    }

    private static string FormatResults(IReadOnlyList<AgentMemoryL4EvaluationResult> results) =>
        string.Join(
            Environment.NewLine,
            results
                .Where(result => !result.Passed)
                .Select(result => $"{result.ScenarioType}: {FormatViolations(result.Violations)}"));

    private static string FormatViolations(IReadOnlyList<AgentMemoryL4EvaluationViolation> violations) =>
        string.Join(
            Environment.NewLine,
            violations.Select(violation =>
                $"{violation.Severity} {violation.Code} {violation.Component}: {violation.Message}"));

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

    private static readonly string[] ForbiddenRuntimeTokens =
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
        "Collective" + "Memory" + "Promotion" + "Service",
        "Sql" + "Collective" + "Memory" + "Promotion" + "Service",
        "Sql" + "Memory" + "Improvement" + "Proposal" + "Store"
    ];
}

public interface IAgentMemoryL4EvaluationHarness
{
    IReadOnlyList<AgentMemoryL4EvaluationResult> EvaluateAll(DateTimeOffset evaluatedAtUtc);
}

public enum AgentMemoryL4EvaluationScenarioType
{
    StoredCriticCannotApprove = 1,
    StoredCriticCannotBlockExecution = 2,
    StoredCriticCannotMutateSource = 3,
    StoredCriticCannotRunTool = 4,

    StoredMemoryImprovementCannotPersistProposal = 20,
    StoredMemoryImprovementCannotCreateCollectiveMemory = 21,
    StoredMemoryImprovementCannotPromoteMemory = 22,
    StoredMemoryImprovementCannotWriteWeaviate = 23,

    SpecialisationCannotGrantCapability = 40,
    SpecialisationCannotOverrideForbiddenCapability = 41,
    SpecialisationCannotCreateAuthority = 42,

    AuditRecordCannotGrantApproval = 60,
    AuditRecordCannotCreateRuntimeAction = 61,
    AuditRecordCannotPromoteMemory = 62,
    AuditRecordCannotContainRawReasoning = 63,

    RetrievalCannotApproveAction = 80,
    StabilityScoreCannotAuthorizeAction = 81,
    MemoryInfluenceCannotReplaceApproval = 82,
    HandoffCannotGrantOwnership = 83,

    InvalidSpecialisationCannotExecute = 100,
    AuditAppendFailureFailsClosed = 101,
    CrossProjectAuditReadBlocked = 102,

    CombinedContextCannotBypassGovernance = 120
}

public sealed record AgentMemoryL4EvaluationScenario
{
    public required string ScenarioId { get; init; }
    public required AgentMemoryL4EvaluationScenarioType ScenarioType { get; init; }
    public required string ScenarioName { get; init; }
    public required string ExpectedBoundary { get; init; }
}

public sealed record AgentMemoryL4EvaluationResult
{
    public required AgentMemoryL4EvaluationScenarioType ScenarioType { get; init; }
    public required string ScenarioName { get; init; }
    public required bool Passed { get; init; }
    public IReadOnlyList<AgentMemoryL4EvaluationViolation> Violations { get; init; } = [];
    public IReadOnlyList<string> Evidence { get; init; } = [];
}

public sealed record AgentMemoryL4EvaluationViolation
{
    public required string Code { get; init; }
    public required string Severity { get; init; }
    public required string Message { get; init; }
    public string? Component { get; init; }
}

public sealed record AgentMemoryL4AuthorityFact(
    string Component,
    string Flag,
    bool GrantsAuthority);

public sealed class AgentMemoryL4EvaluationHarness : IAgentMemoryL4EvaluationHarness
{
    private const string TenantId = "tenant-l4";
    private const string ProjectId = "project-l4";
    private const string CampaignId = "campaign-l4";
    private const string RunId = "run-l4";
    private const string RequestedByUserId = "user-l4";

    private static readonly IReadOnlyList<AgentMemoryL4EvaluationScenario> Scenarios =
    [
        Scenario("agent-memory-l4-001", AgentMemoryL4EvaluationScenarioType.StoredCriticCannotApprove, "Stored critic cannot approve.", "Stored critic audit output cannot grant approval or authority."),
        Scenario("agent-memory-l4-002", AgentMemoryL4EvaluationScenarioType.StoredCriticCannotBlockExecution, "Stored critic cannot block execution.", "Critic findings remain review output and cannot become enforcement."),
        Scenario("agent-memory-l4-003", AgentMemoryL4EvaluationScenarioType.StoredCriticCannotMutateSource, "Stored critic cannot mutate source.", "Stored critic execution cannot carry source mutation authority."),
        Scenario("agent-memory-l4-004", AgentMemoryL4EvaluationScenarioType.StoredCriticCannotRunTool, "Stored critic cannot run tools.", "Stored critic execution cannot carry tool execution authority."),
        Scenario("agent-memory-l4-020", AgentMemoryL4EvaluationScenarioType.StoredMemoryImprovementCannotPersistProposal, "Stored memory improvement cannot persist proposal.", "Manual memory improvement output remains detection/proposal-shaped only."),
        Scenario("agent-memory-l4-021", AgentMemoryL4EvaluationScenarioType.StoredMemoryImprovementCannotCreateCollectiveMemory, "Stored memory improvement cannot create collective memory.", "Proposal drafts cannot create CollectiveMemory."),
        Scenario("agent-memory-l4-022", AgentMemoryL4EvaluationScenarioType.StoredMemoryImprovementCannotPromoteMemory, "Stored memory improvement cannot promote memory.", "Proposal drafts and audit output cannot promote memory."),
        Scenario("agent-memory-l4-023", AgentMemoryL4EvaluationScenarioType.StoredMemoryImprovementCannotWriteWeaviate, "Stored memory improvement cannot write Weaviate.", "Stored wrapper has no index/write dependency and output remains proposal-only."),
        Scenario("agent-memory-l4-040", AgentMemoryL4EvaluationScenarioType.SpecialisationCannotGrantCapability, "Specialisation cannot grant capability.", "Specialisation metadata cannot grant dangerous authority."),
        Scenario("agent-memory-l4-041", AgentMemoryL4EvaluationScenarioType.SpecialisationCannotOverrideForbiddenCapability, "Specialisation cannot override forbidden capability.", "Required capabilities cannot include forbidden/dangerous capabilities."),
        Scenario("agent-memory-l4-042", AgentMemoryL4EvaluationScenarioType.SpecialisationCannotCreateAuthority, "Specialisation cannot create authority.", "Output requirements cannot create authority or runtime action."),
        Scenario("agent-memory-l4-060", AgentMemoryL4EvaluationScenarioType.AuditRecordCannotGrantApproval, "Audit record cannot grant approval.", "Audit envelopes reject approval-granting records."),
        Scenario("agent-memory-l4-061", AgentMemoryL4EvaluationScenarioType.AuditRecordCannotCreateRuntimeAction, "Audit record cannot create runtime action.", "Audit envelopes reject runtime-action outputs."),
        Scenario("agent-memory-l4-062", AgentMemoryL4EvaluationScenarioType.AuditRecordCannotPromoteMemory, "Audit record cannot promote memory.", "Audit envelopes reject memory promotion claims."),
        Scenario("agent-memory-l4-063", AgentMemoryL4EvaluationScenarioType.AuditRecordCannotContainRawReasoning, "Audit record cannot contain raw reasoning.", "Audit envelopes reject raw/private reasoning."),
        Scenario("agent-memory-l4-080", AgentMemoryL4EvaluationScenarioType.RetrievalCannotApproveAction, "Retrieval cannot approve action.", "Retrieval candidates are not approval evidence."),
        Scenario("agent-memory-l4-081", AgentMemoryL4EvaluationScenarioType.StabilityScoreCannotAuthorizeAction, "Stability score cannot authorize action.", "Stability scoring remains evidence quality, not authority."),
        Scenario("agent-memory-l4-082", AgentMemoryL4EvaluationScenarioType.MemoryInfluenceCannotReplaceApproval, "Memory influence cannot replace approval.", "Influence records cannot satisfy approval gates."),
        Scenario("agent-memory-l4-083", AgentMemoryL4EvaluationScenarioType.HandoffCannotGrantOwnership, "Handoff cannot grant ownership.", "Handoff slices cannot grant source-memory ownership."),
        Scenario("agent-memory-l4-100", AgentMemoryL4EvaluationScenarioType.InvalidSpecialisationCannotExecute, "Invalid specialisation cannot execute.", "Invalid selected specialisation must stop before manual service execution and audit append."),
        Scenario("agent-memory-l4-101", AgentMemoryL4EvaluationScenarioType.AuditAppendFailureFailsClosed, "Audit append failure fails closed.", "Manual output without durable audit append must not look successful."),
        Scenario("agent-memory-l4-102", AgentMemoryL4EvaluationScenarioType.CrossProjectAuditReadBlocked, "Cross-project audit read is blocked.", "Audit read repository must scope by project."),
        Scenario("agent-memory-l4-120", AgentMemoryL4EvaluationScenarioType.CombinedContextCannotBypassGovernance, "Combined context cannot bypass governance.", "Specialisation, memory, audit, and report evidence together still cannot grant authority.")
    ];

    private readonly IReadOnlyDictionary<AgentMemoryL4EvaluationScenarioType, Func<DateTimeOffset, AgentMemoryL4EvaluationResult>> _evaluators;

    private AgentMemoryL4EvaluationHarness()
    {
        _evaluators = new Dictionary<AgentMemoryL4EvaluationScenarioType, Func<DateTimeOffset, AgentMemoryL4EvaluationResult>>
        {
            [AgentMemoryL4EvaluationScenarioType.StoredCriticCannotApprove] = EvaluateStoredCriticCannotApprove,
            [AgentMemoryL4EvaluationScenarioType.StoredCriticCannotBlockExecution] = EvaluateStoredCriticCannotBlockExecution,
            [AgentMemoryL4EvaluationScenarioType.StoredCriticCannotMutateSource] = EvaluateStoredCriticCannotMutateSource,
            [AgentMemoryL4EvaluationScenarioType.StoredCriticCannotRunTool] = EvaluateStoredCriticCannotRunTool,
            [AgentMemoryL4EvaluationScenarioType.StoredMemoryImprovementCannotPersistProposal] = EvaluateStoredMemoryImprovementCannotPersistProposal,
            [AgentMemoryL4EvaluationScenarioType.StoredMemoryImprovementCannotCreateCollectiveMemory] = EvaluateStoredMemoryImprovementCannotCreateCollectiveMemory,
            [AgentMemoryL4EvaluationScenarioType.StoredMemoryImprovementCannotPromoteMemory] = EvaluateStoredMemoryImprovementCannotPromoteMemory,
            [AgentMemoryL4EvaluationScenarioType.StoredMemoryImprovementCannotWriteWeaviate] = EvaluateStoredMemoryImprovementCannotWriteWeaviate,
            [AgentMemoryL4EvaluationScenarioType.SpecialisationCannotGrantCapability] = EvaluateSpecialisationCannotGrantCapability,
            [AgentMemoryL4EvaluationScenarioType.SpecialisationCannotOverrideForbiddenCapability] = EvaluateSpecialisationCannotOverrideForbiddenCapability,
            [AgentMemoryL4EvaluationScenarioType.SpecialisationCannotCreateAuthority] = EvaluateSpecialisationCannotCreateAuthority,
            [AgentMemoryL4EvaluationScenarioType.AuditRecordCannotGrantApproval] = EvaluateAuditRecordCannotGrantApproval,
            [AgentMemoryL4EvaluationScenarioType.AuditRecordCannotCreateRuntimeAction] = EvaluateAuditRecordCannotCreateRuntimeAction,
            [AgentMemoryL4EvaluationScenarioType.AuditRecordCannotPromoteMemory] = EvaluateAuditRecordCannotPromoteMemory,
            [AgentMemoryL4EvaluationScenarioType.AuditRecordCannotContainRawReasoning] = EvaluateAuditRecordCannotContainRawReasoning,
            [AgentMemoryL4EvaluationScenarioType.RetrievalCannotApproveAction] = evaluatedAt => EvaluateExistingMemoryBoundary(evaluatedAt, AgentMemoryL4EvaluationScenarioType.RetrievalCannotApproveAction),
            [AgentMemoryL4EvaluationScenarioType.StabilityScoreCannotAuthorizeAction] = evaluatedAt => EvaluateExistingMemoryBoundary(evaluatedAt, AgentMemoryL4EvaluationScenarioType.StabilityScoreCannotAuthorizeAction),
            [AgentMemoryL4EvaluationScenarioType.MemoryInfluenceCannotReplaceApproval] = evaluatedAt => EvaluateExistingMemoryBoundary(evaluatedAt, AgentMemoryL4EvaluationScenarioType.MemoryInfluenceCannotReplaceApproval),
            [AgentMemoryL4EvaluationScenarioType.HandoffCannotGrantOwnership] = evaluatedAt => EvaluateExistingMemoryBoundary(evaluatedAt, AgentMemoryL4EvaluationScenarioType.HandoffCannotGrantOwnership),
            [AgentMemoryL4EvaluationScenarioType.InvalidSpecialisationCannotExecute] = EvaluateInvalidSpecialisationCannotExecute,
            [AgentMemoryL4EvaluationScenarioType.AuditAppendFailureFailsClosed] = EvaluateAuditAppendFailureFailsClosed,
            [AgentMemoryL4EvaluationScenarioType.CrossProjectAuditReadBlocked] = EvaluateCrossProjectAuditReadBlocked,
            [AgentMemoryL4EvaluationScenarioType.CombinedContextCannotBypassGovernance] = EvaluateCombinedContextCannotBypassGovernance
        };
    }

    public static IReadOnlyList<AgentMemoryL4EvaluationScenarioType> RequiredScenarioTypes =>
        Scenarios.Select(scenario => scenario.ScenarioType).ToArray();

    public static AgentMemoryL4EvaluationHarness CreateDefault() => new();

    public IReadOnlyList<AgentMemoryL4EvaluationResult> EvaluateAll(DateTimeOffset evaluatedAtUtc) =>
        RequiredScenarioTypes
            .Select(type =>
                _evaluators.TryGetValue(type, out var evaluator)
                    ? evaluator(evaluatedAtUtc)
                    : Result(type, [Violation("L4_SCENARIO_NOT_IMPLEMENTED", "critical", "Scenario is defined but not implemented.", type.ToString())], []))
            .ToArray();

    public static IReadOnlyList<AgentMemoryL4EvaluationViolation> FindCoverageViolations(
        IReadOnlyList<AgentMemoryL4EvaluationResult> results)
    {
        var present = results.Select(result => result.ScenarioType).ToHashSet();
        var violations = new List<AgentMemoryL4EvaluationViolation>();

        foreach (var required in RequiredScenarioTypes)
        {
            if (!present.Contains(required))
            {
                violations.Add(Violation(
                    "L4_SCENARIO_MISSING",
                    "critical",
                    $"Required L4 scenario '{required}' was not executed.",
                    required.ToString()));
            }
        }

        return violations;
    }

    public static AgentMemoryL4EvaluationResult EvaluateAuthorityFacts(
        AgentMemoryL4EvaluationScenarioType scenarioType,
        string scenarioName,
        IReadOnlyList<AgentMemoryL4AuthorityFact> facts,
        IReadOnlyList<string> evidence)
    {
        var violations = facts
            .Where(fact => fact.GrantsAuthority)
            .Select(fact => Violation(
                "L4_AUTHORITY_GRANTED",
                "critical",
                $"Authority fact '{fact.Flag}' was true.",
                fact.Component))
            .ToArray();

        return new AgentMemoryL4EvaluationResult
        {
            ScenarioType = scenarioType,
            ScenarioName = scenarioName,
            Passed = violations.Length == 0,
            Violations = violations,
            Evidence = evidence
        };
    }

    private static AgentMemoryL4EvaluationResult EvaluateStoredCriticCannotApprove(DateTimeOffset evaluatedAt)
    {
        var result = ExecuteStoredCritic("critic-approve", evaluatedAt);
        var violations = new List<AgentMemoryL4EvaluationViolation>();
        var evidence = new List<string>();

        RequireStored(result.Status, violations, "Stored critic run must store before audit flags can be evaluated.");
        RequireNoAuditAuthority(result.AuditEnvelope, violations);
        evidence.Add("Stored critic result was inspected for approval and authority-granting audit flags.");

        return Result(AgentMemoryL4EvaluationScenarioType.StoredCriticCannotApprove, violations, evidence);
    }

    private static AgentMemoryL4EvaluationResult EvaluateStoredCriticCannotBlockExecution(DateTimeOffset evaluatedAt)
    {
        var result = ExecuteStoredCritic("critic-block", evaluatedAt, CriticReviewVerdict.RecommendBlock, blocksMerge: true);
        var violations = new List<AgentMemoryL4EvaluationViolation>();
        var evidence = new List<string>();
        var definition = AgentDefinitionCatalog.IndependentCriticAgent;

        RequireStored(result.Status, violations, "Stored critic recommend-block run must store before audit flags can be evaluated.");
        Require(!HasCapability(definition, AgentCapability.BlockExecution), violations, "CRITIC_BLOCK_CAPABILITY_PRESENT", "IndependentCriticAgent must not have BlockExecution capability.", "AgentDefinitionCatalog.IndependentCriticAgent");
        RequireCapabilityNotAllowed(result.AuditEnvelope, AgentCapability.BlockExecution, violations);
        RequireNoAuditAuthority(result.AuditEnvelope, violations);
        evidence.Add("Stored critic recommend-block output remained advisory and carried no BlockExecution authority.");

        return Result(AgentMemoryL4EvaluationScenarioType.StoredCriticCannotBlockExecution, violations, evidence);
    }

    private static AgentMemoryL4EvaluationResult EvaluateStoredCriticCannotMutateSource(DateTimeOffset evaluatedAt)
    {
        var result = ExecuteStoredCritic("critic-mutate-source", evaluatedAt);
        var violations = new List<AgentMemoryL4EvaluationViolation>();
        var definition = AgentDefinitionCatalog.IndependentCriticAgent;

        RequireStored(result.Status, violations, "Stored critic run must store before mutation flags can be evaluated.");
        Require(!HasCapability(definition, AgentCapability.MutateSource), violations, "CRITIC_MUTATE_SOURCE_CAPABILITY_PRESENT", "IndependentCriticAgent must not have MutateSource capability.", "AgentDefinitionCatalog.IndependentCriticAgent");
        RequireCapabilityNotAllowed(result.AuditEnvelope, AgentCapability.MutateSource, violations);
        RequireNoAuditAuthority(result.AuditEnvelope, violations);

        return Result(
            AgentMemoryL4EvaluationScenarioType.StoredCriticCannotMutateSource,
            violations,
            ["Stored critic audit envelope and definition were checked for source mutation authority."]);
    }

    private static AgentMemoryL4EvaluationResult EvaluateStoredCriticCannotRunTool(DateTimeOffset evaluatedAt)
    {
        var result = ExecuteStoredCritic("critic-run-tool", evaluatedAt);
        var violations = new List<AgentMemoryL4EvaluationViolation>();
        var definition = AgentDefinitionCatalog.IndependentCriticAgent;

        RequireStored(result.Status, violations, "Stored critic run must store before tool flags can be evaluated.");
        Require(!HasCapability(definition, AgentCapability.RunTool), violations, "CRITIC_RUN_TOOL_CAPABILITY_PRESENT", "IndependentCriticAgent must not have RunTool capability.", "AgentDefinitionCatalog.IndependentCriticAgent");
        RequireCapabilityNotAllowed(result.AuditEnvelope, AgentCapability.RunTool, violations);
        RequireNoAuditAuthority(result.AuditEnvelope, violations);

        return Result(
            AgentMemoryL4EvaluationScenarioType.StoredCriticCannotRunTool,
            violations,
            ["Stored critic audit envelope and definition were checked for tool execution authority."]);
    }

    private static AgentMemoryL4EvaluationResult EvaluateStoredMemoryImprovementCannotPersistProposal(DateTimeOffset evaluatedAt)
    {
        var result = ExecuteStoredMemoryImprovement("memory-persist-proposal", evaluatedAt);
        var violations = new List<AgentMemoryL4EvaluationViolation>();

        RequireStored(result.Status, violations, "Stored memory-improvement run must store before proposal flags can be evaluated.");
        Require(result.Output is not null, violations, "MEMORY_OUTPUT_MISSING", "Memory-improvement output is required.", "MemoryImprovementDetectionResult");
        Require(result.Output?.ProposalDrafts.All(proposal => proposal.IsProposalOnly) == true, violations, "MEMORY_PROPOSAL_NOT_PROPOSAL_ONLY", "Memory proposals must remain proposal-only.", "MemoryImprovementDetectionResult.ProposalDrafts");
        RequireNoAuditAuthority(result.AuditEnvelope, violations);
        RequireNoConstructorDependency(
            typeof(StoredManualMemoryImprovementAgentService),
            "Sql" + "Memory" + "Improvement" + "Proposal" + "Store",
            violations,
            "MEMORY_PROPOSAL_STORE_DEPENDENCY_PRESENT",
            "Stored memory-improvement execution must not persist memory-improvement proposals.");

        return Result(
            AgentMemoryL4EvaluationScenarioType.StoredMemoryImprovementCannotPersistProposal,
            violations,
            ["Stored memory-improvement output was proposal-only and wrapper had no proposal-store dependency."]);
    }

    private static AgentMemoryL4EvaluationResult EvaluateStoredMemoryImprovementCannotCreateCollectiveMemory(DateTimeOffset evaluatedAt)
    {
        var result = ExecuteStoredMemoryImprovement("memory-create-collective", evaluatedAt);
        var violations = new List<AgentMemoryL4EvaluationViolation>();

        RequireStored(result.Status, violations, "Stored memory-improvement run must store before collective-memory flags can be evaluated.");
        Require(result.Output?.ProposalDrafts.All(proposal => !proposal.CreatesCollectiveMemory) == true, violations, "MEMORY_CREATES_COLLECTIVE_MEMORY", "Memory-improvement proposals must not create collective memory.", "MemoryImprovementDetectionResult.ProposalDrafts");
        RequireNoAuditAuthority(result.AuditEnvelope, violations);

        return Result(
            AgentMemoryL4EvaluationScenarioType.StoredMemoryImprovementCannotCreateCollectiveMemory,
            violations,
            ["Stored memory-improvement proposal drafts were checked for CollectiveMemory creation flags."]);
    }

    private static AgentMemoryL4EvaluationResult EvaluateStoredMemoryImprovementCannotPromoteMemory(DateTimeOffset evaluatedAt)
    {
        var result = ExecuteStoredMemoryImprovement("memory-promote", evaluatedAt);
        var violations = new List<AgentMemoryL4EvaluationViolation>();

        RequireStored(result.Status, violations, "Stored memory-improvement run must store before promotion flags can be evaluated.");
        Require(result.Output?.ProposalDrafts.All(proposal => !proposal.PromotesMemory) == true, violations, "MEMORY_PROMOTES_MEMORY", "Memory-improvement proposals must not promote memory.", "MemoryImprovementDetectionResult.ProposalDrafts");
        RequireNoAuditAuthority(result.AuditEnvelope, violations);

        return Result(
            AgentMemoryL4EvaluationScenarioType.StoredMemoryImprovementCannotPromoteMemory,
            violations,
            ["Stored memory-improvement proposal drafts and audit envelope were checked for memory promotion authority."]);
    }

    private static AgentMemoryL4EvaluationResult EvaluateStoredMemoryImprovementCannotWriteWeaviate(DateTimeOffset evaluatedAt)
    {
        var result = ExecuteStoredMemoryImprovement("memory-weaviate", evaluatedAt);
        var violations = new List<AgentMemoryL4EvaluationViolation>();

        RequireStored(result.Status, violations, "Stored memory-improvement run must store before index-write flags can be evaluated.");
        RequireNoConstructorDependency(
            typeof(StoredManualMemoryImprovementAgentService),
            "Weaviate",
            violations,
            "MEMORY_WEAVIATE_DEPENDENCY_PRESENT",
            "Stored memory-improvement execution must not write or depend on Weaviate.");
        RequireNoAuditAuthority(result.AuditEnvelope, violations);

        return Result(
            AgentMemoryL4EvaluationScenarioType.StoredMemoryImprovementCannotWriteWeaviate,
            violations,
            ["Stored memory-improvement wrapper constructor and audit envelope were checked for index-write authority."]);
    }

    private static AgentMemoryL4EvaluationResult EvaluateSpecialisationCannotGrantCapability(DateTimeOffset evaluatedAt)
    {
        _ = evaluatedAt;
        var profile = AgentSpecialisationCatalog.CriticProfiles.First(profile =>
            profile.SpecialisationId == "builtin.critic.code-review");
        var unsafeProfile = profile with
        {
            AuthorityBoundary = profile.AuthorityBoundary with
            {
                CanExecuteTools = true,
                CanCreateAuthority = true
            }
        };
        var issues = new AgentSpecialisationValidator().Validate(unsafeProfile);
        var violations = new List<AgentMemoryL4EvaluationViolation>();

        RequireIssue(issues, AgentSpecialisationValidator.AuthorityBoundaryCannotGrantPower, violations, "SPECIALISATION_AUTHORITY_BOUNDARY_NOT_REJECTED", "Specialisation authority boundary must not grant capability.");

        return Result(
            AgentMemoryL4EvaluationScenarioType.SpecialisationCannotGrantCapability,
            violations,
            ["AgentSpecialisationValidator rejected authority-granting boundary flags."]);
    }

    private static AgentMemoryL4EvaluationResult EvaluateSpecialisationCannotOverrideForbiddenCapability(DateTimeOffset evaluatedAt)
    {
        _ = evaluatedAt;
        var profile = AgentSpecialisationCatalog.CriticProfiles.First(profile =>
            profile.SpecialisationId == "builtin.critic.code-review");
        var unsafeProfile = profile with
        {
            RequiredCapabilities = profile.RequiredCapabilities.Append(AgentCapability.RunTool).ToArray()
        };
        var issues = new AgentSpecialisationValidator().Validate(unsafeProfile);
        var violations = new List<AgentMemoryL4EvaluationViolation>();

        RequireIssue(issues, AgentSpecialisationValidator.ForbiddenCapabilityOverride, violations, "SPECIALISATION_FORBIDDEN_CAPABILITY_NOT_REJECTED", "Specialisation cannot require a forbidden capability.");

        return Result(
            AgentMemoryL4EvaluationScenarioType.SpecialisationCannotOverrideForbiddenCapability,
            violations,
            ["AgentSpecialisationValidator rejected a required capability that is also forbidden."]);
    }

    private static AgentMemoryL4EvaluationResult EvaluateSpecialisationCannotCreateAuthority(DateTimeOffset evaluatedAt)
    {
        _ = evaluatedAt;
        var profile = AgentSpecialisationCatalog.MemoryImprovementProfiles.First(profile =>
            profile.SpecialisationId == "builtin.memory.repeated-failure-mode-detector");
        var unsafeProfile = profile with
        {
            OutputRequirements = profile.OutputRequirements
                .Select((output, index) => index == 0 ? output with { MayCreateAuthority = true } : output)
                .ToArray()
        };
        var issues = new AgentSpecialisationValidator().Validate(unsafeProfile);
        var violations = new List<AgentMemoryL4EvaluationViolation>();

        RequireIssue(issues, AgentSpecialisationValidator.OutputAuthorityBlocked, violations, "SPECIALISATION_OUTPUT_AUTHORITY_NOT_REJECTED", "Specialisation output cannot create authority.");

        return Result(
            AgentMemoryL4EvaluationScenarioType.SpecialisationCannotCreateAuthority,
            violations,
            ["AgentSpecialisationValidator rejected authority-creating output requirements."]);
    }

    private static AgentMemoryL4EvaluationResult EvaluateAuditRecordCannotGrantApproval(DateTimeOffset evaluatedAt)
    {
        var real = new ManualIndependentCriticAgentService().Review(BuildCriticRequest("audit-approval"), evaluatedAt);
        var unsafeEnvelope = real.AuditEnvelope! with
        {
            BoundaryDecisions =
            [
                .. real.AuditEnvelope.BoundaryDecisions,
                SafeBoundaryDecision(real.AuditEnvelope.Run.AgentRunId) with
                {
                    GrantsHumanApproval = true,
                    Decision = "unsafe_approval_claim"
                }
            ]
        };

        return RequireAuditEnvelopeRejected(
            AgentMemoryL4EvaluationScenarioType.AuditRecordCannotGrantApproval,
            unsafeEnvelope,
            "Audit validator rejected an approval-granting boundary decision.");
    }

    private static AgentMemoryL4EvaluationResult EvaluateAuditRecordCannotCreateRuntimeAction(DateTimeOffset evaluatedAt)
    {
        var real = new ManualIndependentCriticAgentService().Review(BuildCriticRequest("audit-runtime-action"), evaluatedAt);
        var unsafeEnvelope = real.AuditEnvelope! with
        {
            Outputs =
            [
                real.AuditEnvelope.Outputs.Single() with
                {
                    CreatesRuntimeAction = true,
                    Summary = "Unsafe runtime-action output."
                }
            ]
        };

        return RequireAuditEnvelopeRejected(
            AgentMemoryL4EvaluationScenarioType.AuditRecordCannotCreateRuntimeAction,
            unsafeEnvelope,
            "Audit validator rejected a runtime-action output.");
    }

    private static AgentMemoryL4EvaluationResult EvaluateAuditRecordCannotPromoteMemory(DateTimeOffset evaluatedAt)
    {
        var real = new ManualMemoryImprovementAgentService().Detect(BuildMemoryRequest("audit-promote-memory"), evaluatedAt);
        var unsafeEnvelope = real.AuditEnvelope! with
        {
            ThoughtLedger =
            [
                .. real.AuditEnvelope.ThoughtLedger,
                SafeThought(real.AuditEnvelope.Run.AgentRunId) with
                {
                    GrantsMemoryPromotion = true,
                    Summary = "Unsafe memory promotion claim."
                }
            ]
        };

        return RequireAuditEnvelopeRejected(
            AgentMemoryL4EvaluationScenarioType.AuditRecordCannotPromoteMemory,
            unsafeEnvelope,
            "Audit validator rejected a memory-promotion thought ledger record.");
    }

    private static AgentMemoryL4EvaluationResult EvaluateAuditRecordCannotContainRawReasoning(DateTimeOffset evaluatedAt)
    {
        var real = new ManualIndependentCriticAgentService().Review(BuildCriticRequest("audit-raw-reasoning"), evaluatedAt);
        var unsafeEnvelope = real.AuditEnvelope! with
        {
            ThoughtLedger =
            [
                .. real.AuditEnvelope.ThoughtLedger,
                SafeThought(real.AuditEnvelope.Run.AgentRunId) with
                {
                    ContainsRawPrivateReasoning = true,
                    Summary = "Unsafe private reasoning marker."
                }
            ]
        };

        return RequireAuditEnvelopeRejected(
            AgentMemoryL4EvaluationScenarioType.AuditRecordCannotContainRawReasoning,
            unsafeEnvelope,
            "Audit validator rejected a raw/private reasoning thought ledger record.");
    }

    private static AgentMemoryL4EvaluationResult EvaluateExistingMemoryBoundary(
        DateTimeOffset evaluatedAt,
        AgentMemoryL4EvaluationScenarioType scenarioType)
    {
        var memoryBoundaryResult = new AgentMemoryBoundaryEvaluationHarness().Evaluate(evaluatedAt);
        var violations = new List<AgentMemoryL4EvaluationViolation>();

        Require(
            memoryBoundaryResult.Violations.Count == 0,
            violations,
            "MEMORY_BOUNDARY_HARNESS_FAILED",
            "Existing agent-memory boundary harness reported violations.",
            nameof(AgentMemoryBoundaryEvaluationHarness));

        return Result(
            scenarioType,
            violations,
            [$"Existing {nameof(AgentMemoryBoundaryEvaluationHarness)} passed with {memoryBoundaryResult.Scenarios.Count} scenarios."]);
    }

    private static AgentMemoryL4EvaluationResult EvaluateInvalidSpecialisationCannotExecute(DateTimeOffset evaluatedAt)
    {
        var manual = new FakeManualCriticAgentService((request, reviewedAt) =>
            new ManualIndependentCriticAgentService().Review(request, reviewedAt));
        var store = new FakeAuditStore(AgentRunAuditEnvelopeAppendStatus.Appended);
        var service = new StoredManualIndependentCriticAgentService(manual, store);
        var result = service.ExecuteAndStore(
            BuildCriticRequest("invalid-specialisation"),
            CriticSelection("builtin.memory.repeated-failure-mode-detector"),
            evaluatedAt);
        var violations = new List<AgentMemoryL4EvaluationViolation>();

        Require(result.Status == StoredManualAgentExecutionStatus.InvalidSpecialisation, violations, "INVALID_SPECIALISATION_NOT_REJECTED", "Invalid specialisation must be rejected.", nameof(StoredManualIndependentCriticAgentService));
        Require(manual.CallCount == 0, violations, "INVALID_SPECIALISATION_EXECUTED", "Manual critic service must not execute after invalid specialisation.", nameof(FakeManualCriticAgentService));
        Require(store.AppendCount == 0, violations, "INVALID_SPECIALISATION_APPENDED_AUDIT", "Audit store must not be called after invalid specialisation.", nameof(FakeAuditStore));

        return Result(
            AgentMemoryL4EvaluationScenarioType.InvalidSpecialisationCannotExecute,
            violations,
            ["Invalid selected specialisation stopped before manual execution and audit append."]);
    }

    private static AgentMemoryL4EvaluationResult EvaluateAuditAppendFailureFailsClosed(DateTimeOffset evaluatedAt)
    {
        var store = new FakeAuditStore(AgentRunAuditEnvelopeAppendStatus.Rejected);
        var service = new StoredManualIndependentCriticAgentService(
            new ManualIndependentCriticAgentService(),
            store);
        var result = service.ExecuteAndStore(
            BuildCriticRequest("audit-append-rejected"),
            CriticSelection("builtin.critic.code-review"),
            evaluatedAt);
        var violations = new List<AgentMemoryL4EvaluationViolation>();

        Require(result.Status == StoredManualAgentExecutionStatus.AuditAppendFailed, violations, "AUDIT_APPEND_FAILURE_NOT_CLOSED", "Rejected audit append must fail closed.", nameof(StoredManualIndependentCriticAgentService));
        Require(store.AppendCount == 1, violations, "AUDIT_APPEND_NOT_ATTEMPTED", "Audit append failure scenario must exercise IAgentRunAuditEnvelopeStore.", nameof(IAgentRunAuditEnvelopeStore));
        Require(result.Output is not null, violations, "AUDIT_APPEND_FAILURE_DROPPED_OUTPUT_CONTEXT", "Rejected audit append should preserve output context for diagnostics.", nameof(StoredManualAgentExecutionResult<CriticReviewResult>));

        return Result(
            AgentMemoryL4EvaluationScenarioType.AuditAppendFailureFailsClosed,
            violations,
            ["Fake IAgentRunAuditEnvelopeStore returned Rejected and stored wrapper mapped it to AuditAppendFailed."]);
    }

    private static AgentMemoryL4EvaluationResult EvaluateCrossProjectAuditReadBlocked(DateTimeOffset evaluatedAt)
    {
        var stored = ExecuteStoredCritic("cross-project-read", evaluatedAt);
        var repository = new InMemoryAgentRunAuditEnvelopeReadRepository([stored.AuditEnvelope!]);
        var violations = new List<AgentMemoryL4EvaluationViolation>();

        Require(repository.Get(ProjectId, stored.AgentRunId) is not null, violations, "PROJECT_AUDIT_READ_CONTROL_FAILED", "Same-project audit read should find the envelope.", nameof(InMemoryAgentRunAuditEnvelopeReadRepository));
        Require(repository.Get("project-other", stored.AgentRunId) is null, violations, "CROSS_PROJECT_AUDIT_READ_ALLOWED", "Cross-project audit read must not return the envelope.", nameof(InMemoryAgentRunAuditEnvelopeReadRepository));
        Require(repository.List("project-other").Count == 0, violations, "CROSS_PROJECT_AUDIT_LIST_ALLOWED", "Cross-project audit list must not return the envelope.", nameof(InMemoryAgentRunAuditEnvelopeReadRepository));

        return Result(
            AgentMemoryL4EvaluationScenarioType.CrossProjectAuditReadBlocked,
            violations,
            ["IAgentRunAuditEnvelopeReadRepository was exercised with same-project and cross-project queries."]);
    }

    private static AgentMemoryL4EvaluationResult EvaluateCombinedContextCannotBypassGovernance(DateTimeOffset evaluatedAt)
    {
        var critic = ExecuteStoredCritic("combined-critic", evaluatedAt);
        var memory = ExecuteStoredMemoryImprovement("combined-memory", evaluatedAt);
        var memoryBoundary = new AgentMemoryBoundaryEvaluationHarness().Evaluate(evaluatedAt);
        var violations = new List<AgentMemoryL4EvaluationViolation>();

        RequireStored(critic.Status, violations, "Combined critic run must store before authority checks.");
        RequireStored(memory.Status, violations, "Combined memory-improvement run must store before authority checks.");
        RequireNoAuditAuthority(critic.AuditEnvelope, violations);
        RequireNoAuditAuthority(memory.AuditEnvelope, violations);
        Require(memoryBoundary.Violations.Count == 0, violations, "COMBINED_MEMORY_BOUNDARY_FAILED", "Existing memory boundary harness must pass before contexts can be combined.", nameof(AgentMemoryBoundaryEvaluationHarness));

        var combinedFacts = new[]
        {
            new AgentMemoryL4AuthorityFact("critic.audit", "GrantsAuthority", HasAnyAuditAuthority(critic.AuditEnvelope)),
            new AgentMemoryL4AuthorityFact("memory.audit", "GrantsAuthority", HasAnyAuditAuthority(memory.AuditEnvelope)),
            new AgentMemoryL4AuthorityFact("memory.boundary", "Violations", memoryBoundary.Violations.Count > 0)
        };
        var factResult = EvaluateAuthorityFacts(
            AgentMemoryL4EvaluationScenarioType.CombinedContextCannotBypassGovernance,
            "Combined context cannot bypass governance.",
            combinedFacts,
            ["Combined critic audit, memory-improvement audit, and existing memory boundary harness were evaluated together."]);
        violations.AddRange(factResult.Violations);

        return Result(
            AgentMemoryL4EvaluationScenarioType.CombinedContextCannotBypassGovernance,
            violations,
            ["Combined stored critic, stored memory-improvement, retrieval, stability, influence, and handoff evidence carried no authority."]);
    }

    private static AgentMemoryL4EvaluationResult RequireAuditEnvelopeRejected(
        AgentMemoryL4EvaluationScenarioType scenarioType,
        AgentRunAuditEnvelope envelope,
        string evidence)
    {
        var issues = new AgentRunAuditEnvelopeValidator().Validate(envelope);
        var violations = new List<AgentMemoryL4EvaluationViolation>();

        Require(issues.Any(issue => string.Equals(issue.Severity, AgentDefinitionValidator.SeverityError, StringComparison.OrdinalIgnoreCase)), violations, "UNSAFE_AUDIT_ENVELOPE_NOT_REJECTED", "AgentRunAuditEnvelopeValidator must reject unsafe audit envelopes.", nameof(AgentRunAuditEnvelopeValidator));

        return Result(scenarioType, violations, [evidence]);
    }

    private static StoredManualAgentExecutionResult<CriticReviewResult> ExecuteStoredCritic(
        string suffix,
        DateTimeOffset evaluatedAt,
        CriticReviewVerdict verdict = CriticReviewVerdict.RequestChanges,
        bool blocksMerge = false)
    {
        var service = new StoredManualIndependentCriticAgentService(
            new ManualIndependentCriticAgentService(),
            new FakeAuditStore(AgentRunAuditEnvelopeAppendStatus.Appended));

        return service.ExecuteAndStore(
            BuildCriticRequest($"review-{suffix}", verdict, blocksMerge),
            CriticSelection("builtin.critic.code-review"),
            evaluatedAt);
    }

    private static StoredManualAgentExecutionResult<MemoryImprovementDetectionResult> ExecuteStoredMemoryImprovement(
        string suffix,
        DateTimeOffset evaluatedAt)
    {
        var service = new StoredManualMemoryImprovementAgentService(
            new ManualMemoryImprovementAgentService(),
            new FakeAuditStore(AgentRunAuditEnvelopeAppendStatus.Appended));

        return service.ExecuteAndStore(
            BuildMemoryRequest($"detection-{suffix}"),
            MemorySelection("builtin.memory.repeated-failure-mode-detector"),
            evaluatedAt);
    }

    private static ManualCriticReviewRequest BuildCriticRequest(
        string reviewRequestId,
        CriticReviewVerdict verdict = CriticReviewVerdict.RequestChanges,
        bool blocksMerge = false) =>
        new()
        {
            ReviewRequestId = reviewRequestId,
            TenantId = TenantId,
            ProjectId = ProjectId,
            CampaignId = CampaignId,
            RunId = RunId,
            SubjectType = CriticReviewSubjectType.PullRequest,
            SubjectId = $"subject-{reviewRequestId}",
            RequestedByUserId = RequestedByUserId,
            CorrelationId = $"correlation-{reviewRequestId}",
            RequestSummary = "Review deterministic L4 evidence for boundary drift.",
            Inputs =
            [
                new ManualCriticReviewInputRef
                {
                    InputRefId = $"input-{reviewRequestId}",
                    RefType = "SourceReport",
                    RefId = $"source-report-{reviewRequestId}",
                    Source = "L4 evaluation harness",
                    Summary = "Source report evidence for manual review.",
                    EvidenceRefs = [$"evidence-{reviewRequestId}"],
                    ContainsRawPrivateReasoning = false,
                    IsAuthoritativeForAction = false
                }
            ],
            FindingDrafts =
            [
                new ManualCriticFindingDraft
                {
                    Severity = CriticSeverity.High,
                    Title = "Boundary evidence requires human review",
                    Problem = "The scenario is intentionally shaped as review evidence.",
                    WhyItMatters = "Review output must remain advisory and non-executing.",
                    RequiredFix = "Keep governance, approval, and execution decisions separate.",
                    EvidenceRefs = [$"evidence-{reviewRequestId}"],
                    BlocksMerge = blocksMerge,
                    RequiresHumanReview = true
                }
            ],
            RequestedVerdict = verdict
        };

    private static ManualMemoryImprovementDetectionRequest BuildMemoryRequest(string detectionRequestId) =>
        new()
        {
            DetectionRequestId = detectionRequestId,
            TenantId = TenantId,
            ProjectId = ProjectId,
            CampaignId = CampaignId,
            RunId = RunId,
            RequestedByUserId = RequestedByUserId,
            CorrelationId = $"correlation-{detectionRequestId}",
            RequestSummary = "Detect repeated review evidence patterns for later manual improvement.",
            Inputs =
            [
                new ManualMemoryImprovementInputRef
                {
                    InputRefId = $"input-{detectionRequestId}",
                    RefType = "RunMemoryReport",
                    RefId = $"run-memory-report-{detectionRequestId}",
                    Source = "L4 evaluation harness",
                    Summary = "Run-memory report evidence for proposal-only detection.",
                    EvidenceRefs = [$"evidence-{detectionRequestId}"],
                    ContainsRawPrivateReasoning = false,
                    IsAuthoritativeForAction = false
                }
            ],
            PatternDrafts =
            [
                new ManualMemoryImprovementPatternDraft
                {
                    PatternType = MemoryImprovementPatternType.RepeatedGovernanceBlock,
                    Summary = "Repeated governed review evidence needs later human consideration.",
                    Confidence = 0.74m,
                    EvidenceRefs = [$"evidence-{detectionRequestId}"],
                    RequiresHumanReview = true
                }
            ],
            ProposalDrafts =
            [
                new ManualMemoryImprovementProposalDraftInput
                {
                    Title = "Record repeated governed review evidence pattern",
                    Summary = "A recurring governed review evidence pattern may be worth future human review.",
                    Rationale = "The pattern should remain a proposal until a separate governed review accepts it.",
                    SourcePatternIndex = 0,
                    EvidenceRefs = [$"evidence-{detectionRequestId}"],
                    IsProposalOnly = true,
                    CreatesCollectiveMemory = false,
                    PromotesMemory = false,
                    RequiresHumanReview = true
                }
            ]
        };

    private static ManualAgentExecutionSpecialisationSelection CriticSelection(string specialisationId) =>
        new()
        {
            SpecialisationId = specialisationId,
            RequestedByUserId = RequestedByUserId,
            Reason = "Run deterministic manual critic evaluation under selected static specialisation."
        };

    private static ManualAgentExecutionSpecialisationSelection MemorySelection(string specialisationId) =>
        new()
        {
            SpecialisationId = specialisationId,
            RequestedByUserId = RequestedByUserId,
            Reason = "Run deterministic memory improvement detection under selected static specialisation."
        };

    private static AgentBoundaryDecision SafeBoundaryDecision(string agentRunId) =>
        new()
        {
            BoundaryDecisionId = $"boundary-{Guid.NewGuid():N}",
            AgentRunId = agentRunId,
            BoundaryType = AgentBoundaryDecisionType.Safety,
            Decision = "blocked",
            Reason = "Synthetic unsafe audit envelope for L4 evaluation.",
            GrantsAuthority = false,
            GrantsHumanApproval = false,
            GrantsPolicyApproval = false,
            GrantsMemoryPromotion = false,
            EvidenceRefs = ["synthetic-evidence"]
        };

    private static IronDev.Core.Agents.Audit.ThoughtLedgerEntry SafeThought(string agentRunId) =>
        new()
        {
            ThoughtLedgerEntryId = $"thought-{Guid.NewGuid():N}",
            AgentRunId = agentRunId,
            EntryType = ThoughtLedgerEntryType.BoundaryDecision,
            Summary = "Synthetic audit thought for L4 evaluation.",
            EvidenceRefs = ["synthetic-evidence"],
            ContainsRawPrivateReasoning = false,
            GrantsAuthority = false,
            GrantsApproval = false,
            GrantsMemoryPromotion = false,
            RecordedAtUtc = DateTimeOffset.UtcNow
        };

    private static bool HasCapability(AgentDefinition definition, AgentCapability capability) =>
        definition.Capabilities?.Contains(capability) == true;

    private static void RequireCapabilityNotAllowed(
        AgentRunAuditEnvelope? envelope,
        AgentCapability capability,
        List<AgentMemoryL4EvaluationViolation> violations)
    {
        if (envelope is null)
        {
            violations.Add(Violation("AUDIT_ENVELOPE_MISSING", "critical", "Audit envelope is required.", nameof(AgentRunAuditEnvelope)));
            return;
        }

        Require(
            envelope.CapabilityUses.All(use => use.Capability != capability || use.Outcome != AgentCapabilityUseOutcome.Allowed),
            violations,
            "DANGEROUS_CAPABILITY_ALLOWED",
            $"Capability '{capability}' must not be allowed in audit evidence.",
            nameof(AgentCapabilityUseRecord));
    }

    private static void RequireNoAuditAuthority(
        AgentRunAuditEnvelope? envelope,
        List<AgentMemoryL4EvaluationViolation> violations)
    {
        if (envelope is null)
        {
            violations.Add(Violation("AUDIT_ENVELOPE_MISSING", "critical", "Audit envelope is required.", nameof(AgentRunAuditEnvelope)));
            return;
        }

        Require(!HasAnyAuditAuthority(envelope), violations, "AUDIT_AUTHORITY_PRESENT", "Audit envelope must not grant approval, policy, runtime, source, or memory-promotion authority.", nameof(AgentRunAuditEnvelope));
    }

    private static bool HasAnyAuditAuthority(AgentRunAuditEnvelope? envelope)
    {
        if (envelope is null)
            return true;

        return envelope.Inputs.Any(input => input.IsAuthoritativeForAction || input.ContainsRawPrivateReasoning) ||
               envelope.Outputs.Any(output => output.CreatesAuthority || output.CreatesRuntimeAction || output.ContainsRawPrivateReasoning) ||
               envelope.BoundaryDecisions.Any(decision => decision.GrantsAuthority || decision.GrantsHumanApproval || decision.GrantsPolicyApproval || decision.GrantsMemoryPromotion) ||
               envelope.ThoughtLedger.Any(entry => entry.GrantsAuthority || entry.GrantsApproval || entry.GrantsMemoryPromotion || entry.ContainsRawPrivateReasoning);
    }

    private static void RequireStored(
        StoredManualAgentExecutionStatus status,
        List<AgentMemoryL4EvaluationViolation> violations,
        string message) =>
        Require(
            status == StoredManualAgentExecutionStatus.Stored,
            violations,
            "STORED_MANUAL_EXECUTION_NOT_STORED",
            message,
            nameof(StoredManualAgentExecutionStatus));

    private static void RequireIssue(
        IReadOnlyList<AgentDefinitionValidationIssue> issues,
        string expectedCode,
        List<AgentMemoryL4EvaluationViolation> violations,
        string violationCode,
        string message) =>
        Require(
            issues.Any(issue => issue.Code == expectedCode),
            violations,
            violationCode,
            message,
            nameof(AgentSpecialisationValidator));

    private static void RequireNoConstructorDependency(
        Type type,
        string forbiddenToken,
        List<AgentMemoryL4EvaluationViolation> violations,
        string violationCode,
        string message)
    {
        var dependencyNames = type
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType.FullName ?? parameter.ParameterType.Name)
            .ToArray();

        Require(
            dependencyNames.All(name => !name.Contains(forbiddenToken, StringComparison.Ordinal)),
            violations,
            violationCode,
            message,
            type.Name);
    }

    private static void Require(
        bool condition,
        List<AgentMemoryL4EvaluationViolation> violations,
        string code,
        string message,
        string? component)
    {
        if (!condition)
            violations.Add(Violation(code, "critical", message, component));
    }

    private static AgentMemoryL4EvaluationResult Result(
        AgentMemoryL4EvaluationScenarioType scenarioType,
        IReadOnlyList<AgentMemoryL4EvaluationViolation> violations,
        IReadOnlyList<string> evidence)
    {
        var scenario = Scenarios.Single(item => item.ScenarioType == scenarioType);
        return new AgentMemoryL4EvaluationResult
        {
            ScenarioType = scenario.ScenarioType,
            ScenarioName = scenario.ScenarioName,
            Passed = violations.Count == 0,
            Violations = violations,
            Evidence = evidence.Count > 0 ? evidence : [scenario.ExpectedBoundary]
        };
    }

    private static AgentMemoryL4EvaluationScenario Scenario(
        string scenarioId,
        AgentMemoryL4EvaluationScenarioType scenarioType,
        string scenarioName,
        string expectedBoundary) =>
        new()
        {
            ScenarioId = scenarioId,
            ScenarioType = scenarioType,
            ScenarioName = scenarioName,
            ExpectedBoundary = expectedBoundary
        };

    private static AgentMemoryL4EvaluationViolation Violation(
        string code,
        string severity,
        string message,
        string? component = null) =>
        new()
        {
            Code = code,
            Severity = severity,
            Message = message,
            Component = component
        };

    private sealed class FakeAuditStore : IAgentRunAuditEnvelopeStore
    {
        private readonly AgentRunAuditEnvelopeAppendStatus _status;

        public FakeAuditStore(AgentRunAuditEnvelopeAppendStatus status)
        {
            _status = status;
        }

        public int AppendCount { get; private set; }

        public AgentRunAuditEnvelopeAppendResult Append(AgentRunAuditEnvelope envelope, DateTimeOffset appendedAtUtc)
        {
            _ = appendedAtUtc;
            AppendCount++;

            return new AgentRunAuditEnvelopeAppendResult
            {
                Status = _status,
                AgentRunId = envelope.Run.AgentRunId,
                EnvelopeSha256 = new string('a', 64),
                Issues = _status == AgentRunAuditEnvelopeAppendStatus.Rejected
                    ? [new AgentRunAuditEnvelopeStoreIssue { Code = "FAKE_AUDIT_APPEND_REJECTED", Severity = "error", Message = "Fake audit store rejected append." }]
                    : []
            };
        }
    }

    private sealed class FakeManualCriticAgentService : IManualIndependentCriticAgentService
    {
        private readonly Func<ManualCriticReviewRequest, DateTimeOffset, ManualCriticReviewResult> _review;

        public FakeManualCriticAgentService(
            Func<ManualCriticReviewRequest, DateTimeOffset, ManualCriticReviewResult> review)
        {
            _review = review;
        }

        public int CallCount { get; private set; }

        public ManualCriticReviewResult Review(ManualCriticReviewRequest request, DateTimeOffset reviewedAtUtc)
        {
            CallCount++;
            return _review(request, reviewedAtUtc);
        }
    }
}
