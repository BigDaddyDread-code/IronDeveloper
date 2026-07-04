namespace IronDev.Core.Orchestration;

public static class OrchestratorContractValidator
{
    public const string ContractRequired = "ORCHESTRATOR_CONTRACT_REQUIRED";
    public const string ContractIdentityRequired = "ORCHESTRATOR_CONTRACT_IDENTITY_REQUIRED";
    public const string AuthorAgentInvalid = "ORCHESTRATOR_AUTHOR_AGENT_INVALID";
    public const string RoleBoundaryMissing = "ORCHESTRATOR_ROLE_BOUNDARY_MISSING";
    public const string ScopeRequired = "ORCHESTRATOR_SCOPE_REQUIRED";
    public const string AcceptanceCriteriaRequired = "ORCHESTRATOR_ACCEPTANCE_CRITERIA_REQUIRED";
    public const string AcceptanceCriterionNotMeasurable = "ORCHESTRATOR_ACCEPTANCE_CRITERION_NOT_MEASURABLE";
    public const string BoundaryMissing = "ORCHESTRATOR_BOUNDARY_MISSING";
    public const string PositiveRoleCapabilityMissing = "ORCHESTRATOR_ROLE_CAPABILITY_MISSING";
    public const string AuthorityFlagForbidden = "ORCHESTRATOR_AUTHORITY_FLAG_FORBIDDEN";
    public const string NextSafeStepInvalid = "ORCHESTRATOR_NEXT_SAFE_STEP_INVALID";
    public const string AuthorityClaimText = "ORCHESTRATOR_AUTHORITY_CLAIM_TEXT";

    private const string BuiltInOrchestratorAgentId = "builtin.orchestrator-ba";

    private static readonly string[] BoundaryFragments =
    [
        "writes the contract",
        "does not judge the result",
        "not approval",
        "not test proof",
        "not critic review",
        "not policy satisfaction",
        "not workflow continuation",
        "not source apply permission",
        "not release readiness",
        "not deployment readiness"
    ];

    private static readonly string[] AuthorityClaimMarkers =
    [
        "approval granted",
        "approved by orchestrator",
        "policy satisfied",
        "tests passed",
        "test proof complete",
        "critic satisfied",
        "critic review complete",
        "source apply authorized",
        "ready to apply",
        "workflow continuation authorized",
        "release ready",
        "deployment ready",
        "contract approved",
        "criteria approved"
    ];

    public static OrchestratorContractValidationResult Validate(OrchestratorWorkContract? contract)
    {
        var result = new OrchestratorContractValidationResult();
        if (contract is null)
        {
            AddIssue(result, ContractRequired, "Orchestrator work contract is required.");
            return result;
        }

        ValidateIdentity(contract, result);
        ValidatePositiveRoleShape(contract, result);
        ValidateForbiddenAuthorityFlags(contract, result);
        ValidateBoundary(contract.Boundary, result);
        ValidateScope(contract, result);
        ValidateAcceptanceCriteria(contract, result);
        ValidateRoleBoundaries(contract, result);
        ValidateNextSafeStep(contract.NextSafeStep, result);
        ValidateAuthorityClaimText(result,
            contract.Title,
            contract.IntentSummary,
            contract.SourceIntentRef,
            contract.AuthorAgentId);
        ValidateAuthorityClaimText(result, contract.Risks, contract.OpenQuestions, contract.RetrievedContextRefs);

        return result;
    }

    private static void ValidateIdentity(OrchestratorWorkContract contract, OrchestratorContractValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(contract.ContractId) ||
            contract.TicketId <= 0 ||
            contract.ProjectId <= 0 ||
            string.IsNullOrWhiteSpace(contract.SourceIntentRef) ||
            string.IsNullOrWhiteSpace(contract.Title) ||
            string.IsNullOrWhiteSpace(contract.IntentSummary))
        {
            AddIssue(result, ContractIdentityRequired, "ContractId, TicketId, ProjectId, SourceIntentRef, Title, and IntentSummary are required.");
        }

        if (!string.Equals(contract.AuthorAgentId, BuiltInOrchestratorAgentId, StringComparison.Ordinal))
        {
            AddIssue(result, AuthorAgentInvalid, "Orchestrator work contracts must be authored by builtin.orchestrator-ba.");
        }
    }

    private static void ValidatePositiveRoleShape(OrchestratorWorkContract contract, OrchestratorContractValidationResult result)
    {
        if (!contract.IsContractAuthor ||
            !contract.IsScopeClarifier ||
            !contract.ShapesAcceptanceCriteria ||
            !contract.RecommendsNextSafeStep ||
            !contract.CoordinatesRoleBoundaries)
        {
            AddIssue(result, PositiveRoleCapabilityMissing, "Orchestrator contract must explicitly describe contract authoring, scope clarification, acceptance-criteria shaping, next-safe-step recommendation, and role-boundary coordination.");
        }
    }

    private static void ValidateForbiddenAuthorityFlags(OrchestratorWorkContract contract, OrchestratorContractValidationResult result)
    {
        if (contract.MutatesSource ||
            contract.AuthorsTests ||
            contract.ActsAsCritic ||
            contract.GrantsApproval ||
            contract.SatisfiesPolicy ||
            contract.AuthorizesWorkflowContinuation ||
            contract.AuthorizesSourceApply ||
            contract.AuthorizesReleaseOrDeployment ||
            contract.JudgesOwnContract)
        {
            AddIssue(result, AuthorityFlagForbidden, "Orchestrator contract cannot claim source mutation, test authoring, critic, approval, policy, workflow continuation, source apply, release/deployment, or self-judgement authority.");
        }
    }

    private static void ValidateBoundary(string boundary, OrchestratorContractValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(boundary))
        {
            AddIssue(result, BoundaryMissing, "Boundary text is required.");
            return;
        }

        foreach (var fragment in BoundaryFragments)
        {
            if (!boundary.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                AddIssue(result, BoundaryMissing, $"Boundary text must state '{fragment}'.");
            }
        }
    }

    private static void ValidateScope(OrchestratorWorkContract contract, OrchestratorContractValidationResult result)
    {
        if (contract.ScopeItems.Count == 0)
        {
            AddIssue(result, ScopeRequired, "At least one scope item is required.");
            return;
        }

        foreach (var item in contract.ScopeItems)
        {
            if (string.IsNullOrWhiteSpace(item.ScopeItemId) || string.IsNullOrWhiteSpace(item.Description))
            {
                AddIssue(result, ScopeRequired, "Every scope item requires ScopeItemId and Description.");
            }

            ValidateAuthorityClaimText(result, item.ScopeItemId, item.Description);
        }
    }

    private static void ValidateAcceptanceCriteria(OrchestratorWorkContract contract, OrchestratorContractValidationResult result)
    {
        if (contract.AcceptanceCriteria.Count == 0)
        {
            AddIssue(result, AcceptanceCriteriaRequired, "At least one acceptance criterion is required.");
            return;
        }

        foreach (var criterion in contract.AcceptanceCriteria)
        {
            if (string.IsNullOrWhiteSpace(criterion.CriterionId) ||
                string.IsNullOrWhiteSpace(criterion.Description) ||
                string.IsNullOrWhiteSpace(criterion.Measure))
            {
                AddIssue(result, AcceptanceCriteriaRequired, "Every acceptance criterion requires CriterionId, Description, and Measure.");
            }

            if (!criterion.IsMeasurable)
            {
                AddIssue(result, AcceptanceCriterionNotMeasurable, "Acceptance criteria must be measurable by another role.");
            }

            ValidateAuthorityClaimText(result, criterion.CriterionId, criterion.Description, criterion.Measure);
        }
    }

    private static void ValidateRoleBoundaries(OrchestratorWorkContract contract, OrchestratorContractValidationResult result)
    {
        foreach (var role in Enum.GetValues<OrchestratorContractRole>())
        {
            if (!contract.RoleBoundaries.Any(boundary => boundary.Role == role))
            {
                AddIssue(result, RoleBoundaryMissing, $"Role boundary for {role} is required.");
            }
        }

        foreach (var boundary in contract.RoleBoundaries)
        {
            if (!Enum.IsDefined(boundary.Role) ||
                string.IsNullOrWhiteSpace(boundary.Responsibility) ||
                string.IsNullOrWhiteSpace(boundary.ForbiddenAuthority))
            {
                AddIssue(result, RoleBoundaryMissing, "Every role boundary requires a known role, responsibility, and forbidden authority statement.");
            }

            ValidateAuthorityClaimText(result, boundary.Responsibility);
        }
    }

    private static void ValidateNextSafeStep(OrchestratorNextSafeStep step, OrchestratorContractValidationResult result)
    {
        if (!Enum.IsDefined(step.Kind) ||
            string.IsNullOrWhiteSpace(step.Recommendation) ||
            !step.IsRecommendationOnly ||
            step.StartsRun ||
            step.ContinuesWorkflow ||
            step.AppliesSource ||
            step.RunsTests ||
            step.RecordsApproval ||
            step.SatisfiesPolicy)
        {
            AddIssue(result, NextSafeStepInvalid, "Next safe step must be a recommendation only and must not start, continue, apply, run tests, record approval, or satisfy policy.");
        }

        ValidateAuthorityClaimText(result, step.RecommendedRole, step.Recommendation);
        ValidateAuthorityClaimText(result, step.RequiredEvidenceRefs);
    }

    private static void ValidateAuthorityClaimText(OrchestratorContractValidationResult result, params IReadOnlyList<string>[] valueSets)
    {
        foreach (var valueSet in valueSets)
        {
            ValidateAuthorityClaimText(result, valueSet.ToArray());
        }
    }

    private static void ValidateAuthorityClaimText(OrchestratorContractValidationResult result, params string?[] values)
    {
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;

            foreach (var marker in AuthorityClaimMarkers)
            {
                if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
                {
                    AddIssue(result, AuthorityClaimText, $"Orchestrator contract text contains authority claim marker '{marker}'.");
                }
            }
        }
    }

    private static void AddIssue(OrchestratorContractValidationResult result, string code, string message)
    {
        var issue = $"{code}: {message}";
        if (!result.Issues.Contains(issue, StringComparer.OrdinalIgnoreCase))
        {
            result.Issues.Add(issue);
        }
    }
}
