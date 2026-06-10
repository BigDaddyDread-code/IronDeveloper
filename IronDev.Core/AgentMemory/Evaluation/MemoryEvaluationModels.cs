namespace IronDev.Core.AgentMemory.Evaluation;

public enum MemoryEvaluationScenarioId
{
    CrossAgentLocalMemoryReadBlocked = 1,
    CrossRunLocalMemoryReadBlocked = 2,
    TerminalMemoryCannotInfluenceAction = 3,
    InfluenceOnlyExpiredMemoryBlocked = 4,
    HandoffDoesNotGrantSourceMemoryAccess = 5,
    NonTargetHandoffUseBlocked = 6,
    ProposalAcceptedDoesNotPromoteMemory = 7,
    ProposalCannotReferenceForeignMemory = 8,
    WeaviateDoesNotIndexRawLocalMemory = 9,
    WeaviateReviewedPositiveDoesNotPromoteMemory = 10,
    SourceMutationNeverAllowedByMemoryAlone = 11,
    ExternalEffectNeverAllowedByMemoryAlone = 12,
    RawReasoningRejectedEverywhere = 13,
    AppendOnlyMutationBlocked = 14,
    RunReportDoesNotLeakOtherRun = 15,
    SiloDoesNotExposeGovernanceOrIndexingServices = 16,
    MemoryBackedExecutionCannotBypassGate = 17,
    MemoryBackedExecutionProducesAuditPackage = 18
}

public sealed record MemoryEvaluationScenarioResult
{
    public required MemoryEvaluationScenarioId ScenarioId { get; init; }

    public required string Name { get; init; }

    public required bool Passed { get; init; }

    public required string Summary { get; init; }

    public required IReadOnlyList<string> Evidence { get; init; }

    public IReadOnlyList<string> FailureReasons { get; init; } = [];
}

public sealed record MemoryEvaluationRunResult
{
    public required string EvaluationRunId { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public required DateTimeOffset CompletedAt { get; init; }

    public required int ScenarioCount { get; init; }

    public required int PassedCount { get; init; }

    public required int FailedCount { get; init; }

    public required IReadOnlyList<MemoryEvaluationScenarioResult> Scenarios { get; init; }
}

public interface IMemoryGovernanceEvaluationHarness
{
    Task<MemoryEvaluationRunResult> RunAsync(
        CancellationToken cancellationToken = default);
}
