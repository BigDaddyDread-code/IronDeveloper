using IronDev.Core.Policy;

namespace IronDev.IntegrationTests.Policy;

[TestClass]
[TestCategory("MissingPolicyFailsClosed")]
public sealed class MissingPolicyFailsClosedTests
{
    private static readonly Guid ProjectId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PolicyId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly string[] SensitiveScopes =
    [
        ProjectApprovalRuleScopes.SourceApply,
        ProjectApprovalRuleScopes.MemoryPromotion,
        ProjectApprovalRuleScopes.ReleaseReadiness,
        ProjectApprovalRuleScopes.ExternalSideEffect,
        ProjectApprovalRuleScopes.DestructiveOperation
    ];

    [TestMethod]
    public void MissingPolicy_ReturnsInvalidPolicyFailClosed() =>
        AssertInvalidPolicy(ValidRequest() with { ProjectPolicy = InvalidPolicy(ProjectId) });

    [TestMethod]
    public void NullPolicy_FailsClosedWithoutPermission()
    {
        var result = new ApprovalRequirementEvaluator().Evaluate(ValidRequest() with { ProjectPolicy = null! });

        Assert.AreEqual(ApprovalRequirementOutcomes.InvalidRequest, result.Outcome);
        AssertFailClosedNoAuthority(result);
        Assert.AreNotEqual(ApprovalRequirementOutcomes.NoApprovalRequired, result.Outcome);
    }

    [TestMethod]
    public void EmptyPolicy_ReturnsInvalidPolicyFailClosed() =>
        AssertInvalidPolicy(ValidRequest() with { ProjectPolicy = InvalidPolicy(ProjectId) with { ProjectAutonomyPolicyId = Guid.Empty, PolicyName = " ", AutonomyLevel = " ", MetadataJson = "{}" } });

    [TestMethod]
    public void PolicyFromDifferentProject_ReturnsInvalidPolicyFailClosed() =>
        AssertInvalidPolicy(ValidRequest() with { ProjectPolicy = ActivePolicy(projectId: Guid.NewGuid()) }, "POLICY_PROJECT_MISMATCH");

    [TestMethod]
    public void PolicyWithUnknownAutonomyLevel_ReturnsInvalidPolicyFailClosed() =>
        AssertInvalidPolicy(ValidRequest() with { ProjectPolicy = ActivePolicy(autonomyLevel: "Loose") });

    [TestMethod]
    public void PolicyWithForbiddenAutonomyLevel_ReturnsInvalidPolicyFailClosed() =>
        AssertInvalidPolicy(ValidRequest() with { ProjectPolicy = ActivePolicy(autonomyLevel: "FullAuto") });

    [TestMethod]
    public void PolicyWithAuthorityGrantingMetadata_ReturnsInvalidPolicyFailClosed() =>
        AssertInvalidPolicy(ValidRequest() with { ProjectPolicy = ActivePolicy(metadataJson: AuthorityMetadata()) });

    [TestMethod]
    public void PolicyWithPrivateReasoningMarkers_ReturnsInvalidPolicyFailClosed() =>
        AssertInvalidPolicy(ValidRequest() with { ProjectPolicy = ActivePolicy(metadataJson: PrivateReasoningMetadata()) });

    [TestMethod] public void DraftPolicy_ReturnsInvalidPolicyFailClosed() => AssertInactivePolicy(nameof(ProjectAutonomyPolicyStatus.Draft));

    [TestMethod] public void RetiredPolicy_ReturnsInvalidPolicyFailClosed() => AssertInactivePolicy(nameof(ProjectAutonomyPolicyStatus.Retired));

    [TestMethod] public void SupersededPolicy_ReturnsInvalidPolicyFailClosed() => AssertInactivePolicy(nameof(ProjectAutonomyPolicyStatus.Superseded));

    [TestMethod]
    public void InactivePolicy_DoesNotEvaluateRules()
    {
        var result = new ApprovalRequirementEvaluator().Evaluate(ValidRequest(ActiveRule(ProjectApprovalRuleApprovalTypes.None, [], scope: ProjectApprovalRuleScopes.ToolExecution)) with
        {
            ProjectPolicy = ActivePolicy(status: nameof(ProjectAutonomyPolicyStatus.Draft))
        });

        AssertInvalidPolicy(result, "POLICY_NOT_ACTIVE");
    }

    [TestMethod]
    public void InactivePolicy_DoesNotReturnNoApprovalRequired()
    {
        var result = new ApprovalRequirementEvaluator().Evaluate(ValidRequest() with { ProjectPolicy = ActivePolicy(status: nameof(ProjectAutonomyPolicyStatus.Retired)) });

        Assert.AreNotEqual(ApprovalRequirementOutcomes.NoApprovalRequired, result.Outcome);
        AssertFailClosedNoAuthority(result);
    }

    [TestMethod]
    public void InactivePolicy_DoesNotExecuteOrApprove() => AssertFailClosedNoAuthority(new ApprovalRequirementEvaluator().Evaluate(ValidRequest() with { ProjectPolicy = ActivePolicy(status: nameof(ProjectAutonomyPolicyStatus.Superseded)) }));

    [TestMethod]
    public void NoApprovalRules_ReturnsNoMatchingRuleFailClosed() => AssertNoMatchingRule(ValidRequest() with { ApprovalRules = [] });

    [TestMethod]
    public void NoMatchingScopeRule_ReturnsNoMatchingRuleFailClosed() => AssertNoMatchingRule(ValidRequest(ActiveRule(scope: ProjectApprovalRuleScopes.ToolExecution)) with { ApprovalScope = ProjectApprovalRuleScopes.SourceApply, ActionName = "source.apply" });

    [TestMethod]
    public void NoMatchingSubjectRule_ReturnsNoMatchingRuleFailClosed() => AssertNoMatchingRule(ValidRequest(ActiveRule(subjectPattern: "different_subject")) with { SubjectType = "tool_request" });

    [TestMethod]
    public void NoMatchingActionRule_ReturnsNoMatchingRuleFailClosed() => AssertNoMatchingRule(ValidRequest(ActiveRule(actionPattern: "different.action")) with { ActionName = "workspace.diff" });

    [TestMethod]
    public void MissingRule_DoesNotReturnNoApprovalRequired() => AssertNoMatchingRule(ValidRequest() with { ApprovalRules = [] });

    [TestMethod]
    public void MissingRule_DoesNotTreatExperimentalAsPermission()
    {
        var result = new ApprovalRequirementEvaluator().Evaluate(ValidRequest() with
        {
            ProjectPolicy = ActivePolicy(autonomyLevel: nameof(ProjectAutonomyLevel.Experimental)),
            ApprovalRules = []
        });

        AssertNoMatchingRule(result);
    }

    [TestMethod]
    public void MissingRule_DoesNotExecuteOrApprove() => AssertFailClosedNoAuthority(new ApprovalRequirementEvaluator().Evaluate(ValidRequest() with { ApprovalRules = [] }));

    [TestMethod] public void RuleFromDifferentProject_ReturnsInvalidPolicyFailClosed() => AssertInvalidPolicy(ValidRequest(ActiveRule(projectId: Guid.NewGuid())), "RULE_PROJECT_MISMATCH");

    [TestMethod] public void RuleForDifferentPolicy_ReturnsInvalidPolicyFailClosed() => AssertInvalidPolicy(ValidRequest(ActiveRule(policyId: Guid.NewGuid())), "RULE_POLICY_MISMATCH");

    [TestMethod] public void RuleWithUnknownScope_ReturnsInvalidPolicyFailClosed() => AssertInvalidPolicy(ValidRequest(ActiveRule(scope: "unknown_scope")), "RULE_INVALID");

    [TestMethod] public void RuleWithUnknownApprovalType_ReturnsInvalidPolicyFailClosed() => AssertInvalidPolicy(ValidRequest(ActiveRule(approvalType: "AutoApprove", approvers: [])), "RULE_INVALID");

    [TestMethod] public void RuleWithUnknownRiskLevel_ReturnsInvalidPolicyFailClosed() => AssertInvalidPolicy(ValidRequest(ActiveRule(risk: "Severe")), "RULE_INVALID");

    [TestMethod] public void RuleWithForbiddenApproverType_ReturnsInvalidPolicyFailClosed() => AssertInvalidPolicy(ValidRequest(ActiveRule(ProjectApprovalRuleApprovalTypes.Single, ["Model"])), "RULE_INVALID");

    [TestMethod] public void RuleWithPrivateReasoningMarkers_ReturnsInvalidPolicyFailClosed() => AssertInvalidPolicy(ValidRequest(ActiveRule(metadataJson: PrivateReasoningMetadata())), "RULE_INVALID");

    [TestMethod] public void RuleWithAuthorityGrantingMetadata_ReturnsInvalidPolicyFailClosed() => AssertInvalidPolicy(ValidRequest(ActiveRule(metadataJson: AuthorityMetadata())), "RULE_INVALID");

    [TestMethod]
    public void InvalidRule_DoesNotGetIgnoredAsSafe()
    {
        var invalidNone = ActiveRule(ProjectApprovalRuleApprovalTypes.None, [], scope: ProjectApprovalRuleScopes.ToolExecution, metadataJson: AuthorityMetadata());
        var result = new ApprovalRequirementEvaluator().Evaluate(ValidRequest(invalidNone));

        AssertInvalidPolicy(result, "RULE_INVALID");
        Assert.AreNotEqual(ApprovalRequirementOutcomes.NoApprovalRequired, result.Outcome);
    }

    [TestMethod] public void SourceApply_MissingRuleFailsClosed() => AssertSensitiveMissingRuleFailsClosed(ProjectApprovalRuleScopes.SourceApply);
    [TestMethod] public void MemoryPromotion_MissingRuleFailsClosed() => AssertSensitiveMissingRuleFailsClosed(ProjectApprovalRuleScopes.MemoryPromotion);
    [TestMethod] public void ReleaseReadiness_MissingRuleFailsClosed() => AssertSensitiveMissingRuleFailsClosed(ProjectApprovalRuleScopes.ReleaseReadiness);
    [TestMethod] public void ExternalSideEffect_MissingRuleFailsClosed() => AssertSensitiveMissingRuleFailsClosed(ProjectApprovalRuleScopes.ExternalSideEffect);
    [TestMethod] public void DestructiveOperation_MissingRuleFailsClosed() => AssertSensitiveMissingRuleFailsClosed(ProjectApprovalRuleScopes.DestructiveOperation);

    [TestMethod] public void SourceApply_ApprovalTypeNoneFailsClosed() => AssertSensitiveNoneFailsClosed(ProjectApprovalRuleScopes.SourceApply);
    [TestMethod] public void MemoryPromotion_ApprovalTypeNoneFailsClosed() => AssertSensitiveNoneFailsClosed(ProjectApprovalRuleScopes.MemoryPromotion);
    [TestMethod] public void ReleaseReadiness_ApprovalTypeNoneFailsClosed() => AssertSensitiveNoneFailsClosed(ProjectApprovalRuleScopes.ReleaseReadiness);
    [TestMethod] public void ExternalSideEffect_ApprovalTypeNoneFailsClosed() => AssertSensitiveNoneFailsClosed(ProjectApprovalRuleScopes.ExternalSideEffect);
    [TestMethod] public void DestructiveOperation_ApprovalTypeNoneFailsClosed() => AssertSensitiveNoneFailsClosed(ProjectApprovalRuleScopes.DestructiveOperation);

    [TestMethod] public void SourceApply_AgentOnlyApproverFailsClosed() => AssertSensitiveNonHumanFailsClosed(ProjectApprovalRuleScopes.SourceApply, ProjectApprovalRuleApproverTypes.Agent);
    [TestMethod] public void MemoryPromotion_AgentOnlyApproverFailsClosed() => AssertSensitiveNonHumanFailsClosed(ProjectApprovalRuleScopes.MemoryPromotion, ProjectApprovalRuleApproverTypes.Agent);
    [TestMethod] public void ReleaseReadiness_SystemOnlyApproverFailsClosed() => AssertSensitiveNonHumanFailsClosed(ProjectApprovalRuleScopes.ReleaseReadiness, ProjectApprovalRuleApproverTypes.System);
    [TestMethod] public void ExternalSideEffect_ModelLikeApproverFailsClosed() => AssertSensitiveNonHumanFailsClosed(ProjectApprovalRuleScopes.ExternalSideEffect, "Model");
    [TestMethod] public void DestructiveOperation_NonHumanApproverFailsClosed() => AssertSensitiveNonHumanFailsClosed(ProjectApprovalRuleScopes.DestructiveOperation, ProjectApprovalRuleApproverTypes.System);

    [TestMethod]
    public void AmbiguousMatchingRules_ReturnsNoMatchingRuleFailClosedOrBlockedByPolicy()
    {
        var first = ActiveRule(ProjectApprovalRuleApprovalTypes.HumanOnly, [ProjectApprovalRuleApproverTypes.Human], ruleId: Guid.NewGuid());
        var second = ActiveRule(ProjectApprovalRuleApprovalTypes.HumanOnly, [ProjectApprovalRuleApproverTypes.ProjectLead], ruleId: Guid.NewGuid());

        var result = new ApprovalRequirementEvaluator().Evaluate(ValidRequest(first, second));

        Assert.AreEqual(ApprovalRequirementOutcomes.NoMatchingRuleFailClosed, result.Outcome);
        AssertFailClosedNoAuthority(result);
    }

    [TestMethod]
    public void AmbiguousRiskLevels_FailClosedOrSelectsMostRestrictive()
    {
        var low = ActiveRule(ProjectApprovalRuleApprovalTypes.Single, [ProjectApprovalRuleApproverTypes.Operator], risk: ProjectApprovalRuleRiskLevels.Low, ruleId: Guid.NewGuid());
        var critical = ActiveRule(ProjectApprovalRuleApprovalTypes.Single, [ProjectApprovalRuleApproverTypes.ProjectLead], risk: ProjectApprovalRuleRiskLevels.Critical, ruleId: Guid.NewGuid());

        var result = new ApprovalRequirementEvaluator().Evaluate(ValidRequest(low, critical));

        Assert.AreEqual(ApprovalRequirementOutcomes.ApprovalRequired, result.Outcome);
        Assert.AreEqual(ProjectApprovalRuleRiskLevels.Critical, result.Requirements.Single().RiskLevel);
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void AmbiguousApprovalTypes_FailClosedOrSelectsMostRestrictive()
    {
        var easy = ActiveRule(ProjectApprovalRuleApprovalTypes.AnyOf, [ProjectApprovalRuleApproverTypes.Operator, ProjectApprovalRuleApproverTypes.ProjectLead], ruleId: Guid.NewGuid());
        var strict = ActiveRule(ProjectApprovalRuleApprovalTypes.HumanOnly, [ProjectApprovalRuleApproverTypes.ProjectLead], ruleId: Guid.NewGuid());

        var result = new ApprovalRequirementEvaluator().Evaluate(ValidRequest(easy, strict));

        Assert.AreEqual(ApprovalRequirementOutcomes.ApprovalRequired, result.Outcome);
        Assert.AreEqual(ProjectApprovalRuleApprovalTypes.HumanOnly, result.Requirements.Single().ApprovalType);
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void AmbiguousSubjectPatterns_FailClosedOrSelectsMostSpecific()
    {
        var wildcard = ActiveRule(subjectPattern: "*", ruleId: Guid.NewGuid());
        var exact = ActiveRule(subjectPattern: "tool_request", ruleId: Guid.NewGuid());

        var result = new ApprovalRequirementEvaluator().Evaluate(ValidRequest(wildcard, exact));

        Assert.AreEqual(ApprovalRequirementOutcomes.ApprovalRequired, result.Outcome);
        Assert.AreEqual(exact.ProjectApprovalRuleId, result.MatchedRuleIds.Single());
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void AmbiguousActionPatterns_FailClosedOrSelectsMostSpecific()
    {
        var wildcard = ActiveRule(actionPattern: "*", ruleId: Guid.NewGuid());
        var exact = ActiveRule(actionPattern: "workspace.diff", ruleId: Guid.NewGuid());

        var result = new ApprovalRequirementEvaluator().Evaluate(ValidRequest(wildcard, exact));

        Assert.AreEqual(ApprovalRequirementOutcomes.ApprovalRequired, result.Outcome);
        Assert.AreEqual(exact.ProjectApprovalRuleId, result.MatchedRuleIds.Single());
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void AmbiguousRuleMatch_DoesNotChooseLeastRestrictiveRule()
    {
        var none = ActiveRule(ProjectApprovalRuleApprovalTypes.None, [], scope: ProjectApprovalRuleScopes.ToolExecution, risk: ProjectApprovalRuleRiskLevels.Low, ruleId: Guid.NewGuid());
        var human = ActiveRule(ProjectApprovalRuleApprovalTypes.HumanOnly, [ProjectApprovalRuleApproverTypes.ProjectLead], scope: ProjectApprovalRuleScopes.ToolExecution, risk: ProjectApprovalRuleRiskLevels.Medium, ruleId: Guid.NewGuid());

        var result = new ApprovalRequirementEvaluator().Evaluate(ValidRequest(none, human));

        Assert.AreNotEqual(ApprovalRequirementOutcomes.NoApprovalRequired, result.Outcome);
        Assert.AreEqual(ProjectApprovalRuleApprovalTypes.HumanOnly, result.Requirements.Single().ApprovalType);
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void AmbiguousRuleMatch_DoesNotReturnNoApprovalRequired() => AmbiguousRuleMatch_DoesNotChooseLeastRestrictiveRule();

    [TestMethod] public void ConservativeProfileTemplate_IsNotActivePolicy() => AssertProfileTemplateIsNotActivePolicy(ProjectPolicyProfileNames.Conservative);
    [TestMethod] public void BalancedProfileTemplate_IsNotActivePolicy() => AssertProfileTemplateIsNotActivePolicy(ProjectPolicyProfileNames.Balanced);
    [TestMethod] public void ExperimentalProfileTemplate_IsNotActivePolicy() => AssertProfileTemplateIsNotActivePolicy(ProjectPolicyProfileNames.Experimental);

    [TestMethod]
    public void GeneratedDraftPolicy_IsNotActivePolicy()
    {
        var draft = new ProjectPolicyProfileFactory().CreateDraftPolicy(ProjectId, ProjectPolicyProfileNames.Balanced, "human", "reviewer");
        var result = new ApprovalRequirementEvaluator().Evaluate(ValidRequest() with { ProjectPolicy = PolicyFromDraft(draft) });

        AssertInvalidPolicy(result, "POLICY_NOT_ACTIVE");
    }

    [TestMethod]
    public void GeneratedDraftRules_AreNotActiveRules()
    {
        var rules = new ProjectPolicyProfileFactory().CreateDraftApprovalRules(ProjectId, PolicyId, ProjectPolicyProfileNames.Balanced, "human", "reviewer")
            .Select(RuleFromDraft)
            .ToArray();

        var result = new ApprovalRequirementEvaluator().Evaluate(ValidRequest() with { ApprovalRules = rules });

        AssertNoMatchingRule(result);
    }

    [TestMethod]
    public void ProfileFactory_DoesNotActivatePolicy()
    {
        var factory = new ProjectPolicyProfileFactory();
        var policy = factory.CreateDraftPolicy(ProjectId, ProjectPolicyProfileNames.Experimental, "human", "reviewer");
        var rules = factory.CreateDraftApprovalRules(ProjectId, PolicyId, ProjectPolicyProfileNames.Experimental, "human", "reviewer");

        Assert.AreEqual(nameof(ProjectAutonomyPolicyStatus.Draft), policy.Status);
        Assert.IsTrue(rules.All(rule => rule.Status == ProjectApprovalRuleStatuses.Draft));
    }

    [TestMethod]
    public void ExperimentalProfile_MissingActivePolicyStillFailsClosed()
    {
        var draft = new ProjectPolicyProfileFactory().CreateDraftPolicy(ProjectId, ProjectPolicyProfileNames.Experimental, "human", "reviewer");
        var result = new ApprovalRequirementEvaluator().Evaluate(ValidRequest() with { ProjectPolicy = PolicyFromDraft(draft) });

        AssertInvalidPolicy(result, "POLICY_NOT_ACTIVE");
    }

    [TestMethod]
    public void ExperimentalProfile_MissingRuleStillFailsClosed()
    {
        var result = new ApprovalRequirementEvaluator().Evaluate(ValidRequest() with { ProjectPolicy = ActivePolicy(autonomyLevel: nameof(ProjectAutonomyLevel.Experimental)), ApprovalRules = [] });

        AssertNoMatchingRule(result);
    }

    [TestMethod] public void ApprovalPackageReadyForReview_DoesNotOverrideMissingPolicy() => AssertPackageDoesNotOverride(ValidRequest() with { ProjectPolicy = InvalidPolicy(ProjectId) });
    [TestMethod] public void ApprovalPackageReadyForReview_DoesNotOverrideMissingRule() => AssertPackageDoesNotOverride(ValidRequest() with { ApprovalRules = [] });
    [TestMethod] public void ApprovalPackageReadyForReview_DoesNotSatisfyApprovalRequirement() => Assert.IsFalse(ReadyForReviewPackage().SatisfiesPolicy);
    [TestMethod] public void ApprovalPackageReadyForReview_DoesNotApproveSourceApply() => Assert.IsFalse(ReadyForReviewPackage().GrantsApproval || ReadyForReviewPackage().MutatesSource);
    [TestMethod] public void ApprovalPackageReadyForReview_DoesNotPromoteMemory() => Assert.IsFalse(ReadyForReviewPackage().PromotesMemory);

    [TestMethod] public void MissingPolicy_ResultAuthorityFlagsAlwaysFalse() => AssertInvalidPolicy(ValidRequest() with { ProjectPolicy = InvalidPolicy(ProjectId) });
    [TestMethod] public void MissingRule_ResultAuthorityFlagsAlwaysFalse() => AssertNoMatchingRule(ValidRequest() with { ApprovalRules = [] });
    [TestMethod] public void InvalidPolicy_ResultAuthorityFlagsAlwaysFalse() => AssertInvalidPolicy(ValidRequest() with { ProjectPolicy = ActivePolicy(autonomyLevel: "Unsafe") });
    [TestMethod] public void InvalidRule_ResultAuthorityFlagsAlwaysFalse() => AssertInvalidPolicy(ValidRequest(ActiveRule(approvalType: "BadType", approvers: [])), "RULE_INVALID");
    [TestMethod] public void AmbiguousPolicy_ResultAuthorityFlagsAlwaysFalse() => AmbiguousMatchingRules_ReturnsNoMatchingRuleFailClosedOrBlockedByPolicy();
    [TestMethod] public void SensitiveScopeFailClosed_ResultAuthorityFlagsAlwaysFalse() => AssertSensitiveNoneFailsClosed(ProjectApprovalRuleScopes.SourceApply);

    [TestMethod]
    [TestCategory("MissingPolicyWording")]
    public void MissingPolicyWording_DoesNotSayDefaultAllow() => AssertDocDoesNotContain("default allow", "allow by default");

    [TestMethod]
    [TestCategory("MissingPolicyWording")]
    public void MissingPolicyWording_DoesNotSayNoRuleMeansAllowed() => AssertDocDoesNotContain("no rule means allowed", "no policy means allowed");

    [TestMethod]
    [TestCategory("MissingPolicyWording")]
    public void MissingPolicyWording_DoesNotSayExperimentalMeansAllowed() => AssertDocDoesNotContain("experimental means allowed");

    [TestMethod]
    [TestCategory("MissingPolicyWording")]
    public void MissingPolicyWording_DoesNotSayProfileMeansActivePolicy() => AssertDocDoesNotContain("profile means active", "draft means active");

    [TestMethod]
    [TestCategory("MissingPolicyWording")]
    public void MissingPolicyWording_DoesNotSayReadyForReviewMeansApproved() => AssertDocDoesNotContain("ready for review means approved");

    [TestMethod]
    [TestCategory("MissingPolicyStaticBoundary")]
    public void MissingPolicy_Static_DoesNotAddApiEndpoint() => AssertDirectoryDoesNotContain("IronDev.Api", "MissingPolicyFailsClosed", "policy-profiles", "approval-rules");

    [TestMethod]
    [TestCategory("MissingPolicyStaticBoundary")]
    public void MissingPolicy_Static_DoesNotAddCliCommand() => AssertDirectoryDoesNotContain(Path.Combine("tools", "IronDev.Cli"), "MissingPolicyFailsClosed", "policy-profile", "approval-rule");

    [TestMethod]
    [TestCategory("MissingPolicyStaticBoundary")]
    public void MissingPolicy_Static_DoesNotAddSqlMigration() => AssertDirectoryDoesNotContain("Database", "MissingPolicyFailsClosed", "project_policy_profile", "approval_rule");

    [TestMethod]
    [TestCategory("MissingPolicyStaticBoundary")]
    public void MissingPolicy_Static_DoesNotAddRepository() => AssertProductionSourceDoesNotContain("MissingPolicyRepository", "ProjectPolicyProfileRepository", "ApprovalRuleRepository");

    [TestMethod]
    [TestCategory("MissingPolicyStaticBoundary")]
    public void MissingPolicy_Static_DoesNotRegisterRuntimeDi()
    {
        var program = File.ReadAllText(Path.Combine(RepositoryRoot(), "IronDev.Api", "Program.cs"));
        Assert.IsFalse(program.Contains("MissingPolicy", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(program.Contains("ProjectPolicyProfileFactory", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    [TestCategory("MissingPolicyStaticBoundary")]
    public void MissingPolicy_Static_DoesNotReferenceWorkflowRunner() => AssertProductionSourceDoesNotContain("IWorkflowRunner", "WorkflowRunner", "ContinueWorkflow");

    [TestMethod]
    [TestCategory("MissingPolicyStaticBoundary")]
    public void MissingPolicy_Static_DoesNotReferenceExecutor() => AssertProductionSourceDoesNotContain("IToolExecutor", "ExecutorService");

    [TestMethod]
    [TestCategory("MissingPolicyStaticBoundary")]
    public void MissingPolicy_Static_DoesNotReferenceSourceApply() => AssertProductionSourceDoesNotContain("SourceApplyService", "IWorkspaceApplyCopy");

    [TestMethod]
    [TestCategory("MissingPolicyStaticBoundary")]
    public void MissingPolicy_Static_DoesNotReferenceMemoryPromotion() => AssertProductionSourceDoesNotContain("MemoryPromotionService", "PromoteCollectiveMemory");

    [TestMethod]
    [TestCategory("MissingPolicyStaticBoundary")]
    public void MissingPolicy_Static_DoesNotReferenceReleaseApproval() => AssertProductionSourceDoesNotContain("ReleaseApprovalService", "MarkReleaseReady");

    [TestMethod]
    [TestCategory("MissingPolicyStaticBoundary")]
    public void MissingPolicy_Static_DoesNotReferenceLangGraph()
    {
        AssertDirectoryDoesNotContain("IronDev.Api", "LangGraph");
        AssertDirectoryDoesNotContain(Path.Combine("tools", "IronDev.Cli"), "LangGraph");
        AssertDirectoryDoesNotContain("Database", "LangGraph");
    }

    [TestMethod]
    [TestCategory("MissingPolicyStaticBoundary")]
    public void MissingPolicy_Static_DoesNotReferenceA2a() => AssertProductionSourceDoesNotContain("A2aMessageBus", "CreateA2aHandoff");

    [TestMethod]
    public void MissingPolicyDocumentation_RecordsFailClosedBoundary()
    {
        var doc = BlockHDoc();

        StringAssert.Contains(doc, "PR87 Missing Policy Fails Closed Tests");
        StringAssert.Contains(doc, "PR87 proves missing policy and missing approval rules fail closed.");
        StringAssert.Contains(doc, "No active policy is not permission.");
        StringAssert.Contains(doc, "No matching rule is not permission.");
        StringAssert.Contains(doc, "Draft policies are not active policies.");
        StringAssert.Contains(doc, "Profile templates are not active policies.");
        StringAssert.Contains(doc, "Experimental is not permission.");
        StringAssert.Contains(doc, "ReadyForReview approval packages do not override missing policy.");
        StringAssert.Contains(doc, "Sensitive scopes require explicit human approval rules.");
    }

    private static void AssertInactivePolicy(string status) => AssertInvalidPolicy(ValidRequest() with { ProjectPolicy = ActivePolicy(status: status) }, "POLICY_NOT_ACTIVE");

    private static void AssertSensitiveMissingRuleFailsClosed(string scope) => AssertNoMatchingRule(ValidRequest(ActiveRule(scope: ProjectApprovalRuleScopes.ToolExecution)) with { ApprovalScope = scope, ActionName = ActionFor(scope) });

    private static void AssertSensitiveNoneFailsClosed(string scope) => AssertInvalidPolicy(ValidRequest(ActiveRule(ProjectApprovalRuleApprovalTypes.None, [], scope: scope, actionPattern: ActionFor(scope), risk: ProjectApprovalRuleRiskLevels.Critical)) with { ApprovalScope = scope, ActionName = ActionFor(scope) }, "RULE_INVALID");

    private static void AssertSensitiveNonHumanFailsClosed(string scope, string approverType) => AssertInvalidPolicy(ValidRequest(ActiveRule(ProjectApprovalRuleApprovalTypes.Single, [approverType], scope: scope, actionPattern: ActionFor(scope), risk: ProjectApprovalRuleRiskLevels.Critical)) with { ApprovalScope = scope, ActionName = ActionFor(scope) }, "RULE_INVALID");

    private static void AssertProfileTemplateIsNotActivePolicy(string profileName)
    {
        var profile = ProjectPolicyProfileCatalog.Get(profileName);

        Assert.AreNotEqual(typeof(ProjectAutonomyPolicy), profile.GetType());
        Assert.IsFalse(profile.GrantsApproval);
        Assert.IsFalse(profile.GrantsExecution);
        Assert.IsFalse(profile.SatisfiesPolicy);
    }

    private static void AssertPackageDoesNotOverride(ApprovalRequirementEvaluationRequest request)
    {
        var package = ReadyForReviewPackage();
        var result = new ApprovalRequirementEvaluator().Evaluate(request);

        Assert.IsFalse(package.GrantsApproval);
        Assert.IsFalse(package.GrantsExecution);
        Assert.IsFalse(package.SatisfiesPolicy);
        Assert.AreNotEqual(ApprovalRequirementOutcomes.NoApprovalRequired, result.Outcome);
        AssertFailClosedNoAuthority(result);
    }

    private static void AssertInvalidPolicy(ApprovalRequirementEvaluationRequest request, string? reasonCode = null) => AssertInvalidPolicy(new ApprovalRequirementEvaluator().Evaluate(request), reasonCode);

    private static void AssertInvalidPolicy(ApprovalRequirementEvaluationResult result, string? reasonCode = null)
    {
        Assert.AreEqual(ApprovalRequirementOutcomes.InvalidPolicyFailClosed, result.Outcome);
        if (reasonCode is not null)
        {
            Assert.AreEqual(reasonCode, result.ReasonCode);
        }

        AssertFailClosedNoAuthority(result);
        Assert.AreNotEqual(ApprovalRequirementOutcomes.NoApprovalRequired, result.Outcome);
    }

    private static void AssertNoMatchingRule(ApprovalRequirementEvaluationRequest request) => AssertNoMatchingRule(new ApprovalRequirementEvaluator().Evaluate(request));

    private static void AssertNoMatchingRule(ApprovalRequirementEvaluationResult result)
    {
        Assert.AreEqual(ApprovalRequirementOutcomes.NoMatchingRuleFailClosed, result.Outcome);
        AssertFailClosedNoAuthority(result);
    }

    private static void AssertFailClosedNoAuthority(ApprovalRequirementEvaluationResult result)
    {
        Assert.IsTrue(result.FailClosed);
        AssertNoAuthority(result);
        Assert.AreEqual(0, result.Requirements.Count);
    }

    private static void AssertNoAuthority(ApprovalRequirementEvaluationResult result)
    {
        Assert.IsFalse(result.GrantsApproval);
        Assert.IsFalse(result.GrantsExecution);
        Assert.IsFalse(result.MutatesSource);
        Assert.IsFalse(result.PromotesMemory);
        Assert.IsFalse(result.StartsWorkflow);
        Assert.IsFalse(result.SatisfiesPolicy);
        Assert.IsFalse(result.TransfersAuthority);
    }

    private static ApprovalRequirementEvaluationRequest ValidRequest(params ProjectApprovalRule[] rules) =>
        new()
        {
            ProjectId = ProjectId,
            ProjectPolicy = ActivePolicy(),
            ApprovalRules = rules.Length == 0 ? [ActiveRule()] : rules,
            ApprovalScope = ProjectApprovalRuleScopes.ToolExecution,
            SubjectType = "tool_request",
            SubjectId = "tool-request-1",
            ActionName = "workspace.diff",
            RequestedByActorType = "human",
            RequestedByActorId = "human-reviewer",
            ContextVersion = 1,
            ContextJson = """
                {
                  "schema": "approval.requirement.context.v1",
                  "notes": "Safe evaluation context."
                }
                """
        };

    private static ProjectAutonomyPolicy ActivePolicy(
        Guid? projectId = null,
        string autonomyLevel = nameof(ProjectAutonomyLevel.Balanced),
        string status = nameof(ProjectAutonomyPolicyStatus.Active),
        string? metadataJson = null) =>
        new()
        {
            ProjectAutonomyPolicyId = PolicyId,
            ProjectId = projectId ?? ProjectId,
            PolicyName = "Project authority policy",
            PolicyVersion = 1,
            AutonomyLevel = autonomyLevel,
            Status = status,
            CreatedByActorType = "human",
            CreatedByActorId = "human-reviewer",
            MetadataVersion = 1,
            MetadataJson = metadataJson ?? SafePolicyMetadata(),
            CreatedUtc = DateTimeOffset.UtcNow
        };

    private static ProjectAutonomyPolicy InvalidPolicy(Guid projectId) =>
        ActivePolicy(projectId: projectId) with
        {
            ProjectAutonomyPolicyId = Guid.Empty,
            PolicyName = " ",
            AutonomyLevel = "Unknown",
            MetadataJson = "{}"
        };

    private static ProjectAutonomyPolicy PolicyFromDraft(ProjectAutonomyPolicyCreateRequest request) =>
        new()
        {
            ProjectAutonomyPolicyId = PolicyId,
            ProjectId = request.ProjectId,
            PolicyName = request.PolicyName,
            PolicyVersion = request.PolicyVersion,
            AutonomyLevel = request.AutonomyLevel,
            Status = request.Status,
            SupersedesPolicyId = request.SupersedesPolicyId,
            CreatedByActorType = request.CreatedByActorType,
            CreatedByActorId = request.CreatedByActorId,
            MetadataVersion = request.MetadataVersion,
            MetadataJson = request.MetadataJson,
            CreatedUtc = DateTimeOffset.UtcNow
        };

    private static ProjectApprovalRule ActiveRule(
        string approvalType = ProjectApprovalRuleApprovalTypes.Single,
        IReadOnlyList<string>? approvers = null,
        string scope = ProjectApprovalRuleScopes.ToolExecution,
        string risk = ProjectApprovalRuleRiskLevels.Medium,
        string subjectPattern = "tool_request",
        string actionPattern = "workspace.diff",
        Guid? ruleId = null,
        Guid? projectId = null,
        Guid? policyId = null,
        string? metadataJson = null) =>
        new()
        {
            ProjectApprovalRuleId = ruleId ?? Guid.Parse("33333333-3333-3333-3333-333333333333"),
            ProjectId = projectId ?? ProjectId,
            ProjectAutonomyPolicyId = policyId ?? PolicyId,
            RuleName = "Project approval rule",
            RuleVersion = 1,
            Status = ProjectApprovalRuleStatuses.Active,
            ApprovalScope = scope,
            SubjectTypePattern = subjectPattern,
            ActionNamePattern = actionPattern,
            RiskLevel = risk,
            ApprovalType = approvalType,
            ApproverTypes = approvers ?? [ProjectApprovalRuleApproverTypes.ProjectLead],
            CreatedByActorType = "human",
            CreatedByActorId = "human-reviewer",
            MetadataVersion = 1,
            MetadataJson = metadataJson ?? SafeRuleMetadata(),
            CreatedUtc = DateTimeOffset.UtcNow
        };

    private static ProjectApprovalRule RuleFromDraft(ProjectApprovalRuleCreateRequest request) =>
        new()
        {
            ProjectApprovalRuleId = Guid.NewGuid(),
            ProjectId = request.ProjectId,
            ProjectAutonomyPolicyId = request.ProjectAutonomyPolicyId,
            RuleName = request.RuleName,
            RuleVersion = request.RuleVersion,
            Status = request.Status,
            ApprovalScope = request.ApprovalScope,
            SubjectTypePattern = request.SubjectTypePattern,
            ActionNamePattern = request.ActionNamePattern,
            RiskLevel = request.RiskLevel,
            ApprovalType = request.ApprovalType,
            ApproverTypes = request.ApproverTypes,
            QuorumCount = request.QuorumCount,
            SupersedesRuleId = request.SupersedesRuleId,
            CreatedByActorType = request.CreatedByActorType,
            CreatedByActorId = request.CreatedByActorId,
            MetadataVersion = request.MetadataVersion,
            MetadataJson = request.MetadataJson,
            CreatedUtc = DateTimeOffset.UtcNow
        };

    private static ApprovalPackage ReadyForReviewPackage() =>
        new()
        {
            ApprovalPackageId = Guid.NewGuid(),
            ProjectId = ProjectId,
            PackageName = "Review package",
            PackageVersion = 1,
            Status = ApprovalPackageStatuses.ReadyForReview,
            ApprovalScope = ProjectApprovalRuleScopes.SourceApply,
            SubjectType = "SourceApplyPackage",
            SubjectId = "source-apply-1",
            ActionName = "source.apply",
            SourceEvaluationId = Guid.NewGuid(),
            SourceEvaluationOutcome = ApprovalRequirementOutcomes.ApprovalRequired,
            Requirements =
            [
                new ApprovalPackageRequirement
                {
                    ApprovalScope = ProjectApprovalRuleScopes.SourceApply,
                    ApprovalType = ProjectApprovalRuleApprovalTypes.HumanOnly,
                    RiskLevel = ProjectApprovalRuleRiskLevels.Critical,
                    RequiredApproverTypes = [ProjectApprovalRuleApproverTypes.ProjectLead],
                    RequirementCode = "APPROVAL_REQUIRED_BY_RULE",
                    RequirementReason = "Requirement was derived from evaluator output.",
                    SourceRuleId = Guid.NewGuid()
                }
            ],
            EvidenceReferences =
            [
                new ApprovalPackageEvidenceReference
                {
                    EvidenceType = ApprovalPackageEvidenceTypes.GovernanceEvent,
                    EvidenceId = "governance-event-1"
                }
            ],
            CreatedByActorType = "human",
            CreatedByActorId = "human-reviewer",
            MetadataVersion = 1,
            MetadataJson = """
                {
                  "schema": "approval.package.metadata.v1",
                  "notes": "Safe package metadata."
                }
                """,
            GrantsApproval = false,
            GrantsExecution = false,
            MutatesSource = false,
            PromotesMemory = false,
            StartsWorkflow = false,
            SatisfiesPolicy = false,
            TransfersAuthority = false,
            CreatedUtc = DateTimeOffset.UtcNow
        };

    private static string ActionFor(string scope) => scope switch
    {
        ProjectApprovalRuleScopes.SourceApply => "source.apply",
        ProjectApprovalRuleScopes.MemoryPromotion => "memory.promote",
        ProjectApprovalRuleScopes.ReleaseReadiness => "release.review",
        ProjectApprovalRuleScopes.ExternalSideEffect => "external.effect",
        ProjectApprovalRuleScopes.DestructiveOperation => "destructive.operation",
        _ => "workspace.diff"
    };

    private static string SafePolicyMetadata() =>
        """
        {
          "schema": "project.autonomy.policy.metadata.v1",
          "notes": "Safe policy metadata."
        }
        """;

    private static string SafeRuleMetadata() =>
        """
        {
          "schema": "project.approval.rule.metadata.v1",
          "notes": "Safe rule metadata."
        }
        """;

    private static string AuthorityMetadata() =>
        """
        {
          "schema": "unsafe.metadata.v1",
          "grantsApproval": true
        }
        """;

    private static string PrivateReasoningMetadata() =>
        """
        {
          "schema": "unsafe.metadata.v1",
          "hiddenReasoning": "private reasoning must not influence policy."
        }
        """;

    private static void AssertDocDoesNotContain(params string[] phrases)
    {
        var doc = BlockHDoc();
        foreach (var phrase in phrases)
        {
            Assert.IsFalse(doc.Contains(phrase, StringComparison.OrdinalIgnoreCase), phrase);
        }
    }

    private static string BlockHDoc() => File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "BLOCK_H_PROJECT_AUTHORITY_POLICY_MODEL.md"));

    private static void AssertProductionSourceDoesNotContain(params string[] tokens)
    {
        var files = Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "IronDev.Core", "Policy"), "*.cs", SearchOption.AllDirectories);
        var text = string.Join(Environment.NewLine, files.Select(File.ReadAllText));

        foreach (var token in tokens)
        {
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), token);
        }
    }

    private static void AssertDirectoryDoesNotContain(string relativeDirectory, params string[] tokens)
    {
        var directory = Path.Combine(RepositoryRoot(), relativeDirectory);
        if (!Directory.Exists(directory))
        {
            return;
        }

        var text = string.Join(Environment.NewLine, Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .Select(File.ReadAllText));

        foreach (var token in tokens)
        {
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), token);
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}
