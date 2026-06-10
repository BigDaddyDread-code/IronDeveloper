namespace IronDev.Core.Agents.Evaluation;

public enum AgentMemoryBoundaryScenarioType
{
    ProposalAgentCannotPromoteMemory = 1,
    ReviewAgentCannotBlockExecutionDirectly = 2,
    RetrievalAgentCannotApproveAction = 3,
    StabilityScoreCannotAuthorizeAction = 4,
    RetrievedAcceptedMemoryCannotApproveToolExecution = 5,
    MemoryImprovementDraftCannotCreateCollectiveMemory = 6,
    HumanProxyRequiresExplicitHumanEvent = 7,
    PersonaCannotImplyApproval = 8,
    GovernanceAgentCannotExecuteGovernedAction = 9,
    ImplementationAgentCannotPromoteMemory = 10,
    CriticFindingCannotEnforceBlock = 11,
    CollectiveRetrievalCannotSatisfyPolicyApproval = 12,
    LocalMemoryInfluenceCannotReplaceApproval = 13,
    HandoffCannotGrantMemoryOwnership = 14,
    ProposalAcceptedStatusCannotPromoteCollectiveMemory = 15,
    WeaviateIndexingCannotCreateAuthority = 16
}

public sealed record AgentMemoryBoundaryScenario
{
    public required string ScenarioId { get; init; }

    public required AgentMemoryBoundaryScenarioType ScenarioType { get; init; }

    public required string Description { get; init; }

    public required string ExpectedBoundary { get; init; }

    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
}

public sealed record AgentMemoryBoundaryViolation
{
    public required string ScenarioId { get; init; }

    public required string Code { get; init; }

    public required string Message { get; init; }

    public required bool IsCritical { get; init; }
}

public sealed record AgentMemoryBoundaryEvaluationResult
{
    public required string EvaluationRunId { get; init; }

    public required IReadOnlyList<AgentMemoryBoundaryScenario> Scenarios { get; init; }

    public required IReadOnlyList<AgentMemoryBoundaryViolation> Violations { get; init; }

    public required DateTimeOffset EvaluatedAt { get; init; }

    public bool Passed => Violations.Count == 0;
}

public interface IAgentMemoryBoundaryEvaluationHarness
{
    AgentMemoryBoundaryEvaluationResult Evaluate(DateTimeOffset evaluatedAt);
}
