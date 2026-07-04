namespace IronDev.Core.Orchestration;

public enum OrchestratorContractRole
{
    Builder = 1,
    Tester = 2,
    Critic = 3,
    HumanGate = 4
}

public enum OrchestratorNextSafeStepKind
{
    ClarifyScope = 1,
    RecommendRoleHandoff = 2,
    RequestHumanDecision = 3,
    StopForMissingEvidence = 4
}

public sealed record class OrchestratorWorkContract
{
    public string ContractId { get; init; } = string.Empty;
    public long TicketId { get; init; }
    public int ProjectId { get; init; }

    public string AuthorAgentId { get; init; } = "builtin.orchestrator-ba";
    public string SourceIntentRef { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;
    public string IntentSummary { get; init; } = string.Empty;

    public IReadOnlyList<OrchestratorScopeItem> ScopeItems { get; init; } = [];
    public IReadOnlyList<OrchestratorAcceptanceCriterion> AcceptanceCriteria { get; init; } = [];
    public IReadOnlyList<OrchestratorRoleBoundary> RoleBoundaries { get; init; } = [];
    public IReadOnlyList<string> Risks { get; init; } = [];
    public IReadOnlyList<string> OpenQuestions { get; init; } = [];
    public IReadOnlyList<string> RetrievedContextRefs { get; init; } = [];

    public OrchestratorNextSafeStep NextSafeStep { get; init; } = new();

    public bool IsContractAuthor { get; init; } = true;
    public bool IsScopeClarifier { get; init; } = true;
    public bool ShapesAcceptanceCriteria { get; init; } = true;
    public bool RecommendsNextSafeStep { get; init; } = true;
    public bool CoordinatesRoleBoundaries { get; init; } = true;

    public bool MutatesSource { get; init; }
    public bool AuthorsTests { get; init; }
    public bool ActsAsCritic { get; init; }
    public bool GrantsApproval { get; init; }
    public bool SatisfiesPolicy { get; init; }
    public bool AuthorizesWorkflowContinuation { get; init; }
    public bool AuthorizesSourceApply { get; init; }
    public bool AuthorizesReleaseOrDeployment { get; init; }
    public bool JudgesOwnContract { get; init; }

    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;

    public string Boundary { get; init; } = BoundaryText;

    public const string BoundaryText =
        "The Orchestrator writes the contract. It does not judge the result. " +
        "The contract is not approval, not test proof, not critic review, not policy satisfaction, " +
        "not workflow continuation, not source apply permission, not release readiness, and not deployment readiness.";
}

public sealed record class OrchestratorScopeItem
{
    public string ScopeItemId { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public bool InScope { get; init; } = true;
    public string Boundary { get; init; } =
        "Scope clarifies work. Scope does not authorize source mutation or workflow continuation.";
}

public sealed record class OrchestratorAcceptanceCriterion
{
    public string CriterionId { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Measure { get; init; } = string.Empty;
    public bool IsMeasurable { get; init; } = true;
    public string Boundary { get; init; } =
        "Acceptance criteria describe how another role may measure the work. They are not approval or test proof.";
}

public sealed record class OrchestratorRoleBoundary
{
    public OrchestratorContractRole Role { get; init; }
    public string Responsibility { get; init; } = string.Empty;
    public string ForbiddenAuthority { get; init; } = string.Empty;
}

public sealed record class OrchestratorNextSafeStep
{
    public OrchestratorNextSafeStepKind Kind { get; init; } = OrchestratorNextSafeStepKind.ClarifyScope;
    public string RecommendedRole { get; init; } = string.Empty;
    public string Recommendation { get; init; } = string.Empty;
    public IReadOnlyList<string> RequiredEvidenceRefs { get; init; } = [];

    public bool IsRecommendationOnly { get; init; } = true;
    public bool StartsRun { get; init; }
    public bool ContinuesWorkflow { get; init; }
    public bool AppliesSource { get; init; }
    public bool RunsTests { get; init; }
    public bool RecordsApproval { get; init; }
    public bool SatisfiesPolicy { get; init; }
}

public sealed class OrchestratorContractValidationResult
{
    public bool IsValid => Issues.Count == 0;
    public List<string> Issues { get; } = [];
    public List<string> Warnings { get; } = [];
}
