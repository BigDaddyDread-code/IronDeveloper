using System.Text.Json;

namespace IronDev.Core.Policy;

public static class ProjectPolicyProfileNames
{
    public const string Conservative = "Conservative";
    public const string Balanced = "Balanced";
    public const string Experimental = "Experimental";

    public static IReadOnlyList<string> All { get; } = [Conservative, Balanced, Experimental];

    public static IReadOnlyList<string> Forbidden { get; } =
    [
        "Free",
        "Unrestricted",
        "FullAuto",
        "NoApproval",
        "GodMode",
        "Unlimited",
        "Autonomous",
        "Unsafe"
    ];

    public static bool IsAllowed(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && All.Any(profile => string.Equals(profile, value.Trim(), StringComparison.OrdinalIgnoreCase));

    public static bool IsForbidden(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && Forbidden.Any(profile => string.Equals(profile, value.Trim(), StringComparison.OrdinalIgnoreCase));

    public static string Normalize(string value) =>
        All.First(profile => string.Equals(profile, value.Trim(), StringComparison.OrdinalIgnoreCase));
}

public sealed record ProjectPolicyProfile
{
    public required string ProfileName { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
    public required string AutonomyLevel { get; init; }
    public required int MetadataVersion { get; init; }
    public required string MetadataJson { get; init; }
    public required IReadOnlyList<ProjectPolicyProfileRuleTemplate> RuleTemplates { get; init; }
    public required bool GrantsApproval { get; init; }
    public required bool GrantsExecution { get; init; }
    public required bool MutatesSource { get; init; }
    public required bool PromotesMemory { get; init; }
    public required bool StartsWorkflow { get; init; }
    public required bool SatisfiesPolicy { get; init; }
    public required bool TransfersAuthority { get; init; }
}

public sealed record ProjectPolicyProfileRuleTemplate
{
    public required string RuleName { get; init; }
    public required string ApprovalScope { get; init; }
    public string? SubjectTypePattern { get; init; }
    public string? ActionNamePattern { get; init; }
    public required string RiskLevel { get; init; }
    public required string ApprovalType { get; init; }
    public required IReadOnlyList<string> ApproverTypes { get; init; }
    public int? QuorumCount { get; init; }
    public required int MetadataVersion { get; init; }
    public required string MetadataJson { get; init; }
}

public sealed record ProjectPolicyProfileSummary
{
    public required string ProfileName { get; init; }
    public required string DisplayName { get; init; }
    public required string AutonomyLevel { get; init; }
    public required int RuleTemplateCount { get; init; }
}

public sealed record ProjectPolicyProfileValidationIssue(string Code, string Field, string Message);

public sealed record ProjectPolicyProfileValidationResult(IReadOnlyList<ProjectPolicyProfileValidationIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;
}

public interface IProjectPolicyProfileFactory
{
    ProjectAutonomyPolicyCreateRequest CreateDraftPolicy(Guid projectId, string profileName, string createdByActorType, string createdByActorId);

    IReadOnlyList<ProjectApprovalRuleCreateRequest> CreateDraftApprovalRules(Guid projectId, Guid projectAutonomyPolicyId, string profileName, string createdByActorType, string createdByActorId);
}

public sealed class ProjectPolicyProfileFactory : IProjectPolicyProfileFactory
{
    public ProjectAutonomyPolicyCreateRequest CreateDraftPolicy(Guid projectId, string profileName, string createdByActorType, string createdByActorId)
    {
        ValidateInputs(projectId, profileName, createdByActorType, createdByActorId);
        var profile = ValidProfile(profileName);

        return new ProjectAutonomyPolicyCreateRequest
        {
            ProjectId = projectId,
            PolicyName = $"{profile.DisplayName} project authority policy draft",
            PolicyVersion = 1,
            AutonomyLevel = profile.AutonomyLevel,
            Status = nameof(ProjectAutonomyPolicyStatus.Draft),
            CreatedByActorType = createdByActorType.Trim(),
            CreatedByActorId = createdByActorId.Trim(),
            MetadataVersion = 1,
            MetadataJson = Serialize(new SortedDictionary<string, object?>
            {
                ["schema"] = "project.autonomy.policy.profile.seed.v1",
                ["profile"] = profile.ProfileName,
                ["notes"] = "Draft policy created from a starter profile template."
            })
        };
    }

    public IReadOnlyList<ProjectApprovalRuleCreateRequest> CreateDraftApprovalRules(Guid projectId, Guid projectAutonomyPolicyId, string profileName, string createdByActorType, string createdByActorId)
    {
        ValidateInputs(projectId, profileName, createdByActorType, createdByActorId);
        if (projectAutonomyPolicyId == Guid.Empty)
        {
            throw new ArgumentException("Project autonomy policy ID is required.", nameof(projectAutonomyPolicyId));
        }

        var profile = ValidProfile(profileName);
        return profile.RuleTemplates.Select(template => new ProjectApprovalRuleCreateRequest
        {
            ProjectId = projectId,
            ProjectAutonomyPolicyId = projectAutonomyPolicyId,
            RuleName = template.RuleName,
            RuleVersion = 1,
            Status = ProjectApprovalRuleStatuses.Draft,
            ApprovalScope = template.ApprovalScope,
            SubjectTypePattern = template.SubjectTypePattern,
            ActionNamePattern = template.ActionNamePattern,
            RiskLevel = template.RiskLevel,
            ApprovalType = template.ApprovalType,
            ApproverTypes = template.ApproverTypes,
            QuorumCount = template.QuorumCount,
            CreatedByActorType = createdByActorType.Trim(),
            CreatedByActorId = createdByActorId.Trim(),
            MetadataVersion = template.MetadataVersion,
            MetadataJson = template.MetadataJson
        }).ToArray();
    }

    private static ProjectPolicyProfile ValidProfile(string profileName)
    {
        var profile = ProjectPolicyProfileCatalog.Get(profileName);
        var validation = ProjectPolicyProfileValidator.Validate(profile);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(string.Join(", ", validation.Issues.Select(issue => issue.Code)));
        }

        return profile;
    }

    private static void ValidateInputs(Guid projectId, string profileName, string createdByActorType, string createdByActorId)
    {
        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("Project ID is required.", nameof(projectId));
        }

        if (!ProjectPolicyProfileNames.IsAllowed(profileName))
        {
            throw new ArgumentException("Profile name is not part of the bounded profile vocabulary.", nameof(profileName));
        }

        if (string.IsNullOrWhiteSpace(createdByActorType))
        {
            throw new ArgumentException("Created-by actor type is required.", nameof(createdByActorType));
        }

        if (string.IsNullOrWhiteSpace(createdByActorId))
        {
            throw new ArgumentException("Created-by actor ID is required.", nameof(createdByActorId));
        }
    }

    private static string Serialize(SortedDictionary<string, object?> value) => JsonSerializer.Serialize(value);
}

public static class ProjectPolicyProfileCatalog
{
    private static readonly IReadOnlyList<ProjectPolicyProfile> Profiles =
    [
        Profile(ProjectPolicyProfileNames.Conservative, "Most changes require explicit human review, including all sensitive scopes.", nameof(ProjectAutonomyLevel.Conservative),
        [
            Rule("Conservative tool execution review", ProjectApprovalRuleScopes.ToolExecution, ProjectApprovalRuleRiskLevels.Medium, ProjectApprovalRuleApprovalTypes.Single, [ProjectApprovalRuleApproverTypes.Operator], "tool_request", "tool.*"),
            Rule("Conservative proposal acceptance review", ProjectApprovalRuleScopes.ProposalAcceptance, ProjectApprovalRuleRiskLevels.Medium, ProjectApprovalRuleApprovalTypes.Single, [ProjectApprovalRuleApproverTypes.ProjectLead], "memory_proposal", "proposal.accept"),
            Rule("Conservative dogfood receipt classification review", ProjectApprovalRuleScopes.DogfoodReceiptClassification, ProjectApprovalRuleRiskLevels.Medium, ProjectApprovalRuleApprovalTypes.Single, [ProjectApprovalRuleApproverTypes.Operator], "dogfood_receipt", "dogfood.classify"),
            Rule("Conservative workflow step routing review", ProjectApprovalRuleScopes.WorkflowStepRouting, ProjectApprovalRuleRiskLevels.High, ProjectApprovalRuleApprovalTypes.HumanOnly, [ProjectApprovalRuleApproverTypes.ProjectLead], "workflow_step", "workflow.route"),
            Rule("Conservative A2A handoff validation review", ProjectApprovalRuleScopes.A2aHandoffValidation, ProjectApprovalRuleRiskLevels.High, ProjectApprovalRuleApprovalTypes.HumanOnly, [ProjectApprovalRuleApproverTypes.ProjectLead], "a2a_handoff", "a2a.validate"),
            Sensitive("Conservative source apply review", ProjectApprovalRuleScopes.SourceApply, ProjectApprovalRuleApprovalTypes.HumanOnly, [ProjectApprovalRuleApproverTypes.ProjectLead], "source_apply_package", "source.apply"),
            Sensitive("Conservative memory promotion review", ProjectApprovalRuleScopes.MemoryPromotion, ProjectApprovalRuleApprovalTypes.HumanOnly, [ProjectApprovalRuleApproverTypes.MemoryOwner], "memory_promotion_package", "memory.promote"),
            Sensitive("Conservative release readiness review", ProjectApprovalRuleScopes.ReleaseReadiness, ProjectApprovalRuleApprovalTypes.AllOf, [ProjectApprovalRuleApproverTypes.ProjectLead, ProjectApprovalRuleApproverTypes.ReleaseOwner], "release_readiness_package", "release.review"),
            Sensitive("Conservative external side effect review", ProjectApprovalRuleScopes.ExternalSideEffect, ProjectApprovalRuleApprovalTypes.HumanOnly, [ProjectApprovalRuleApproverTypes.SecurityOwner], "external_effect_package", "external.effect"),
            Sensitive("Conservative destructive operation review", ProjectApprovalRuleScopes.DestructiveOperation, ProjectApprovalRuleApprovalTypes.HumanOnly, [ProjectApprovalRuleApproverTypes.SecurityOwner], "destructive_operation_package", "destructive.operation")
        ]),
        Profile(ProjectPolicyProfileNames.Balanced, "Routine non-sensitive changes can use broader reviewer pools, while sensitive scopes still require human review.", nameof(ProjectAutonomyLevel.Balanced),
        [
            Rule("Balanced tool execution review", ProjectApprovalRuleScopes.ToolExecution, ProjectApprovalRuleRiskLevels.Medium, ProjectApprovalRuleApprovalTypes.AnyOf, [ProjectApprovalRuleApproverTypes.Operator, ProjectApprovalRuleApproverTypes.ProjectLead], "tool_request", "tool.*"),
            Rule("Balanced proposal acceptance review", ProjectApprovalRuleScopes.ProposalAcceptance, ProjectApprovalRuleRiskLevels.Medium, ProjectApprovalRuleApprovalTypes.AnyOf, [ProjectApprovalRuleApproverTypes.Operator, ProjectApprovalRuleApproverTypes.ProjectLead], "memory_proposal", "proposal.accept"),
            Rule("Balanced dogfood receipt classification review", ProjectApprovalRuleScopes.DogfoodReceiptClassification, ProjectApprovalRuleRiskLevels.Low, ProjectApprovalRuleApprovalTypes.AnyOf, [ProjectApprovalRuleApproverTypes.Operator, ProjectApprovalRuleApproverTypes.ProjectLead], "dogfood_receipt", "dogfood.classify"),
            Rule("Balanced workflow step routing review", ProjectApprovalRuleScopes.WorkflowStepRouting, ProjectApprovalRuleRiskLevels.High, ProjectApprovalRuleApprovalTypes.Single, [ProjectApprovalRuleApproverTypes.ProjectLead], "workflow_step", "workflow.route"),
            Rule("Balanced A2A handoff validation review", ProjectApprovalRuleScopes.A2aHandoffValidation, ProjectApprovalRuleRiskLevels.High, ProjectApprovalRuleApprovalTypes.HumanOnly, [ProjectApprovalRuleApproverTypes.ProjectLead], "a2a_handoff", "a2a.validate"),
            Sensitive("Balanced source apply review", ProjectApprovalRuleScopes.SourceApply, ProjectApprovalRuleApprovalTypes.HumanOnly, [ProjectApprovalRuleApproverTypes.ProjectLead], "source_apply_package", "source.apply"),
            Sensitive("Balanced memory promotion review", ProjectApprovalRuleScopes.MemoryPromotion, ProjectApprovalRuleApprovalTypes.HumanOnly, [ProjectApprovalRuleApproverTypes.MemoryOwner], "memory_promotion_package", "memory.promote"),
            Sensitive("Balanced release readiness review", ProjectApprovalRuleScopes.ReleaseReadiness, ProjectApprovalRuleApprovalTypes.AllOf, [ProjectApprovalRuleApproverTypes.ProjectLead, ProjectApprovalRuleApproverTypes.ReleaseOwner], "release_readiness_package", "release.review"),
            Sensitive("Balanced external side effect review", ProjectApprovalRuleScopes.ExternalSideEffect, ProjectApprovalRuleApprovalTypes.HumanOnly, [ProjectApprovalRuleApproverTypes.SecurityOwner], "external_effect_package", "external.effect"),
            Sensitive("Balanced destructive operation review", ProjectApprovalRuleScopes.DestructiveOperation, ProjectApprovalRuleApprovalTypes.HumanOnly, [ProjectApprovalRuleApproverTypes.SecurityOwner], "destructive_operation_package", "destructive.operation")
        ]),
        Profile(ProjectPolicyProfileNames.Experimental, "Non-sensitive proposal and receipt classification can be lighter, but sensitive scopes still require human review.", nameof(ProjectAutonomyLevel.Experimental),
        [
            Rule("Experimental tool execution review", ProjectApprovalRuleScopes.ToolExecution, ProjectApprovalRuleRiskLevels.Medium, ProjectApprovalRuleApprovalTypes.AnyOf, [ProjectApprovalRuleApproverTypes.Operator, ProjectApprovalRuleApproverTypes.ProjectLead], "tool_request", "tool.*"),
            Rule("Experimental proposal acceptance template", ProjectApprovalRuleScopes.ProposalAcceptance, ProjectApprovalRuleRiskLevels.Low, ProjectApprovalRuleApprovalTypes.None, [], "memory_proposal", "proposal.accept"),
            Rule("Experimental dogfood receipt classification template", ProjectApprovalRuleScopes.DogfoodReceiptClassification, ProjectApprovalRuleRiskLevels.Low, ProjectApprovalRuleApprovalTypes.None, [], "dogfood_receipt", "dogfood.classify"),
            Rule("Experimental workflow step routing review", ProjectApprovalRuleScopes.WorkflowStepRouting, ProjectApprovalRuleRiskLevels.High, ProjectApprovalRuleApprovalTypes.Single, [ProjectApprovalRuleApproverTypes.ProjectLead], "workflow_step", "workflow.route"),
            Rule("Experimental A2A handoff validation review", ProjectApprovalRuleScopes.A2aHandoffValidation, ProjectApprovalRuleRiskLevels.High, ProjectApprovalRuleApprovalTypes.HumanOnly, [ProjectApprovalRuleApproverTypes.ProjectLead], "a2a_handoff", "a2a.validate"),
            Sensitive("Experimental source apply review", ProjectApprovalRuleScopes.SourceApply, ProjectApprovalRuleApprovalTypes.HumanOnly, [ProjectApprovalRuleApproverTypes.ProjectLead], "source_apply_package", "source.apply"),
            Sensitive("Experimental memory promotion review", ProjectApprovalRuleScopes.MemoryPromotion, ProjectApprovalRuleApprovalTypes.HumanOnly, [ProjectApprovalRuleApproverTypes.MemoryOwner], "memory_promotion_package", "memory.promote"),
            Sensitive("Experimental release readiness review", ProjectApprovalRuleScopes.ReleaseReadiness, ProjectApprovalRuleApprovalTypes.AllOf, [ProjectApprovalRuleApproverTypes.ProjectLead, ProjectApprovalRuleApproverTypes.ReleaseOwner], "release_readiness_package", "release.review"),
            Sensitive("Experimental external side effect review", ProjectApprovalRuleScopes.ExternalSideEffect, ProjectApprovalRuleApprovalTypes.HumanOnly, [ProjectApprovalRuleApproverTypes.SecurityOwner], "external_effect_package", "external.effect"),
            Sensitive("Experimental destructive operation review", ProjectApprovalRuleScopes.DestructiveOperation, ProjectApprovalRuleApprovalTypes.HumanOnly, [ProjectApprovalRuleApproverTypes.SecurityOwner], "destructive_operation_package", "destructive.operation")
        ])
    ];

    public static IReadOnlyList<ProjectPolicyProfile> All => Profiles;

    public static IReadOnlyList<ProjectPolicyProfileSummary> Summaries => Profiles.Select(profile => new ProjectPolicyProfileSummary
    {
        ProfileName = profile.ProfileName,
        DisplayName = profile.DisplayName,
        AutonomyLevel = profile.AutonomyLevel,
        RuleTemplateCount = profile.RuleTemplates.Count
    }).ToArray();

    public static ProjectPolicyProfile? Find(string? profileName) =>
        ProjectPolicyProfileNames.IsAllowed(profileName)
            ? Profiles.Single(profile => profile.ProfileName == ProjectPolicyProfileNames.Normalize(profileName!))
            : null;

    public static ProjectPolicyProfile Get(string profileName) =>
        Find(profileName) ?? throw new ArgumentException("Profile name is not part of the bounded profile vocabulary.", nameof(profileName));

    private static ProjectPolicyProfile Profile(string profileName, string description, string autonomyLevel, IReadOnlyList<ProjectPolicyProfileRuleTemplate> rules) => new()
    {
        ProfileName = profileName,
        DisplayName = profileName,
        Description = description,
        AutonomyLevel = autonomyLevel,
        MetadataVersion = 1,
        MetadataJson = JsonSerializer.Serialize(new SortedDictionary<string, object?>
        {
            ["schema"] = "project.policy.profile.metadata.v1",
            ["profile"] = profileName,
            ["notes"] = $"{profileName} starter template. Template only.",
            ["grantsApproval"] = false,
            ["grantsExecution"] = false,
            ["mutatesSource"] = false,
            ["promotesMemory"] = false,
            ["startsWorkflow"] = false,
            ["satisfiesPolicy"] = false,
            ["transfersAuthority"] = false
        }),
        RuleTemplates = rules,
        GrantsApproval = false,
        GrantsExecution = false,
        MutatesSource = false,
        PromotesMemory = false,
        StartsWorkflow = false,
        SatisfiesPolicy = false,
        TransfersAuthority = false
    };

    private static ProjectPolicyProfileRuleTemplate Sensitive(string ruleName, string scope, string approvalType, IReadOnlyList<string> approvers, string subject, string action) =>
        Rule(ruleName, scope, ProjectApprovalRuleRiskLevels.Critical, approvalType, approvers, subject, action);

    private static ProjectPolicyProfileRuleTemplate Rule(string ruleName, string scope, string risk, string approvalType, IReadOnlyList<string> approvers, string subject, string action) => new()
    {
        RuleName = ruleName,
        ApprovalScope = scope,
        SubjectTypePattern = subject,
        ActionNamePattern = action,
        RiskLevel = risk,
        ApprovalType = approvalType,
        ApproverTypes = approvers,
        MetadataVersion = 1,
        MetadataJson = JsonSerializer.Serialize(new SortedDictionary<string, object?>
        {
            ["schema"] = "project.policy.profile.rule.metadata.v1",
            ["notes"] = "Draft rule from profile template."
        })
    };
}

public static class ProjectPolicyProfileValidator
{
    private static readonly string[] PrivateReasoningMarkers =
    [
        "hiddenReasoning",
        "hidden reasoning",
        "chainOfThought",
        "chain of thought",
        "scratchpad",
        "privateReasoning",
        "private reasoning",
        "rawPrompt",
        "raw prompt",
        "rawCompletion",
        "raw completion"
    ];

    private static readonly string[] AuthorityMarkers =
    [
        "autoApprove",
        "auto approve",
        "autoExecute",
        "auto execute",
        "approved",
        "authorized",
        "canExecute",
        "can execute",
        "executionAllowed",
        "execution allowed",
        "policySatisfied",
        "policy satisfied",
        "releaseReady",
        "release ready",
        "canShip",
        "can ship",
        "sourceApplyAllowed",
        "source apply allowed",
        "memoryPromotionAllowed",
        "memory promotion allowed",
        "permission granted",
        "full auto"
    ];

    private static readonly string[] FalseOnlyAuthorityProperties =
    [
        "grantsApproval",
        "grantsExecution",
        "mutatesSource",
        "promotesMemory",
        "startsWorkflow",
        "satisfiesPolicy",
        "transfersAuthority"
    ];

    public static ProjectPolicyProfileValidationResult Validate(ProjectPolicyProfile? profile)
    {
        var issues = new List<ProjectPolicyProfileValidationIssue>();
        if (profile is null)
        {
            Add(issues, "PROFILE_REQUIRED", "profile", "Project policy profile is required.");
            return new ProjectPolicyProfileValidationResult(issues);
        }

        if (string.IsNullOrWhiteSpace(profile.ProfileName))
        {
            Add(issues, "PROFILE_NAME_REQUIRED", nameof(profile.ProfileName), "Profile name is required.");
        }
        else if (ProjectPolicyProfileNames.IsForbidden(profile.ProfileName))
        {
            Add(issues, "PROFILE_NAME_FORBIDDEN", nameof(profile.ProfileName), "Profile name is explicitly forbidden.");
        }
        else if (!ProjectPolicyProfileNames.IsAllowed(profile.ProfileName))
        {
            Add(issues, "PROFILE_NAME_UNKNOWN", nameof(profile.ProfileName), "Profile name is not part of the bounded vocabulary.");
        }

        if (!Enum.GetNames<ProjectAutonomyLevel>().Any(level => string.Equals(level, profile.AutonomyLevel, StringComparison.OrdinalIgnoreCase)))
        {
            Add(issues, "AUTONOMY_LEVEL_UNKNOWN", nameof(profile.AutonomyLevel), "Autonomy level is not part of the bounded vocabulary.");
        }
        else if (ProjectPolicyProfileNames.IsAllowed(profile.ProfileName)
            && !string.Equals(ProjectPolicyProfileNames.Normalize(profile.ProfileName), profile.AutonomyLevel, StringComparison.OrdinalIgnoreCase))
        {
            Add(issues, "PROFILE_AUTONOMY_MISMATCH", nameof(profile.AutonomyLevel), "Profile name and autonomy level must match.");
        }

        ValidateNoAuthorityFlags(issues, profile);
        ValidateText(issues, nameof(profile.DisplayName), profile.DisplayName);
        ValidateText(issues, nameof(profile.Description), profile.Description);
        ValidateMetadata(issues, nameof(profile.MetadataJson), profile.MetadataVersion, profile.MetadataJson, allowFalseAuthorityFlags: true);
        ValidateRules(issues, profile.RuleTemplates);

        return new ProjectPolicyProfileValidationResult(issues);
    }

    private static void ValidateRules(List<ProjectPolicyProfileValidationIssue> issues, IReadOnlyList<ProjectPolicyProfileRuleTemplate>? rules)
    {
        if (rules is null || rules.Count == 0)
        {
            Add(issues, "RULE_TEMPLATES_REQUIRED", nameof(ProjectPolicyProfile.RuleTemplates), "At least one rule template is required.");
            return;
        }

        foreach (var (rule, index) in rules.Select((rule, index) => (rule, index)))
        {
            var path = $"RuleTemplates[{index}]";
            ValidateText(issues, $"{path}.RuleName", rule.RuleName);
            ValidateMetadata(issues, $"{path}.MetadataJson", rule.MetadataVersion, rule.MetadataJson, allowFalseAuthorityFlags: false);

            if (!ProjectApprovalRuleScopes.IsAllowed(rule.ApprovalScope))
            {
                Add(issues, "APPROVAL_SCOPE_UNKNOWN", $"{path}.ApprovalScope", "Approval scope is not part of the bounded vocabulary.");
            }

            if (!ProjectApprovalRuleRiskLevels.IsAllowed(rule.RiskLevel))
            {
                Add(issues, "RISK_LEVEL_UNKNOWN", $"{path}.RiskLevel", "Risk level is not part of the bounded vocabulary.");
            }

            if (!ProjectApprovalRuleApprovalTypes.IsAllowed(rule.ApprovalType))
            {
                Add(issues, "APPROVAL_TYPE_UNKNOWN", $"{path}.ApprovalType", "Approval type is not part of the bounded vocabulary.");
            }

            var sensitive = ProjectApprovalRuleScopes.IsSensitive(rule.ApprovalScope);
            if (sensitive && string.Equals(rule.ApprovalType, ProjectApprovalRuleApprovalTypes.None, StringComparison.OrdinalIgnoreCase))
            {
                Add(issues, "SENSITIVE_SCOPE_REQUIRES_HUMAN_APPROVAL", $"{path}.ApprovalType", "Sensitive scopes cannot use ApprovalType=None.");
            }

            if (ProjectApprovalRuleApprovalTypes.RequiresApprovers(rule.ApprovalType) && rule.ApproverTypes.Count == 0)
            {
                Add(issues, "APPROVERS_REQUIRED", $"{path}.ApproverTypes", "This approval type requires approver types.");
            }

            if (string.Equals(rule.ApprovalType, ProjectApprovalRuleApprovalTypes.None, StringComparison.OrdinalIgnoreCase) && rule.ApproverTypes.Count > 0)
            {
                Add(issues, "APPROVERS_NOT_ALLOWED", $"{path}.ApproverTypes", "ApprovalType=None cannot carry approver types.");
            }

            foreach (var approverType in rule.ApproverTypes)
            {
                if (ProjectApprovalRuleApproverTypes.IsForbidden(approverType)
                    || approverType.Equals("ToolGateDecision", StringComparison.OrdinalIgnoreCase)
                    || approverType.Equals("PolicyDecisionEvent", StringComparison.OrdinalIgnoreCase))
                {
                    Add(issues, "APPROVER_TYPE_FORBIDDEN", $"{path}.ApproverTypes", "Gate, policy, model, workflow, or runtime artifacts are not approvers.");
                }
                else if (!ProjectApprovalRuleApproverTypes.IsAllowed(approverType))
                {
                    Add(issues, "APPROVER_TYPE_UNKNOWN", $"{path}.ApproverTypes", "Approver type is not part of the bounded vocabulary.");
                }
            }

            if (sensitive && rule.ApproverTypes.Any(ProjectApprovalRuleApproverTypes.IsAutomated))
            {
                Add(issues, "SENSITIVE_SCOPE_REJECTS_AUTOMATED_APPROVER", $"{path}.ApproverTypes", "Sensitive scopes cannot use System or Agent approver types.");
            }

            if (sensitive && !rule.ApproverTypes.Any(ProjectApprovalRuleApproverTypes.IsHumanClass))
            {
                Add(issues, "SENSITIVE_SCOPE_REQUIRES_HUMAN_APPROVER", $"{path}.ApproverTypes", "Sensitive scopes require a human approver class.");
            }
        }
    }

    private static void ValidateMetadata(List<ProjectPolicyProfileValidationIssue> issues, string field, int version, string? metadataJson, bool allowFalseAuthorityFlags)
    {
        if (version <= 0)
        {
            Add(issues, "METADATA_VERSION_REQUIRED", field, "Metadata version must be positive.");
        }

        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            Add(issues, "METADATA_JSON_REQUIRED", field, "Metadata JSON is required.");
            return;
        }

        if (metadataJson.Length > 8192)
        {
            Add(issues, "METADATA_TOO_LARGE", field, "Metadata JSON must stay small.");
        }

        ValidateText(issues, field, metadataJson);

        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                Add(issues, "METADATA_JSON_OBJECT_REQUIRED", field, "Metadata JSON must be an object.");
                return;
            }

            if (!document.RootElement.TryGetProperty("schema", out var schema)
                || schema.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(schema.GetString()))
            {
                Add(issues, "METADATA_SCHEMA_REQUIRED", field, "Metadata JSON requires a schema field.");
            }

            ScanElement(issues, document.RootElement, field, allowFalseAuthorityFlags);
        }
        catch (JsonException)
        {
            Add(issues, "METADATA_JSON_INVALID", field, "Metadata JSON is not valid JSON.");
        }
    }

    private static void ScanElement(List<ProjectPolicyProfileValidationIssue> issues, JsonElement element, string path, bool allowFalseAuthorityFlags)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var propertyPath = $"{path}.{property.Name}";
                ValidateText(issues, propertyPath, property.Name);
                if (FalseOnlyAuthorityProperties.Contains(property.Name, StringComparer.OrdinalIgnoreCase)
                    && (!allowFalseAuthorityFlags || property.Value.ValueKind != JsonValueKind.False))
                {
                    Add(issues, "METADATA_AUTHORITY_GRANT", propertyPath, "Authority metadata must not grant approval, execution, source mutation, memory promotion, workflow, policy satisfaction, or authority transfer.");
                }

                ScanElement(issues, property.Value, propertyPath, allowFalseAuthorityFlags);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
            {
                ScanElement(issues, item, $"{path}[{index}]", allowFalseAuthorityFlags);
                index++;
            }
        }
        else if (element.ValueKind == JsonValueKind.String)
        {
            ValidateText(issues, path, element.GetString());
        }
    }

    private static void ValidateNoAuthorityFlags(List<ProjectPolicyProfileValidationIssue> issues, ProjectPolicyProfile profile)
    {
        if (profile.GrantsApproval || profile.GrantsExecution || profile.MutatesSource || profile.PromotesMemory || profile.StartsWorkflow || profile.SatisfiesPolicy || profile.TransfersAuthority)
        {
            Add(issues, "PROFILE_AUTHORITY_FLAG_TRUE", "authority", "Project policy profiles cannot grant approval, execution, source mutation, memory promotion, workflow, policy satisfaction, or authority transfer.");
        }
    }

    private static void ValidateText(List<ProjectPolicyProfileValidationIssue> issues, string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Add(issues, "TEXT_REQUIRED", field, "Text value is required.");
            return;
        }

        if (ProjectPolicyProfileNames.IsForbidden(value))
        {
            Add(issues, "TEXT_FORBIDDEN_PROFILE_NAME", field, "Text cannot use a forbidden profile name.");
        }

        if (ContainsAny(value, PrivateReasoningMarkers))
        {
            Add(issues, "TEXT_PRIVATE_REASONING", field, "Text cannot contain hidden or private reasoning markers.");
        }

        if (ContainsAny(value, AuthorityMarkers))
        {
            Add(issues, "TEXT_AUTHORITY_WORDING", field, "Text cannot contain approval, execution, policy, source apply, or memory promotion authority wording.");
        }
    }

    private static bool ContainsAny(string value, IEnumerable<string> markers) =>
        markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static void Add(List<ProjectPolicyProfileValidationIssue> issues, string code, string field, string message) =>
        issues.Add(new ProjectPolicyProfileValidationIssue(code, field, message));
}
