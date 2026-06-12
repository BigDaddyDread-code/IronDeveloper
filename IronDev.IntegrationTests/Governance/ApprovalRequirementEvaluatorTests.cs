using System.Reflection;
using IronDev.Core.Policy;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("ApprovalRequirementEvaluator")]
public sealed class ApprovalRequirementEvaluatorTests
{
    private readonly ApprovalRequirementEvaluator _evaluator = new();

    [TestMethod]
    public void ApprovalRequirementEvaluator_ExposesPureEvaluationContract()
    {
        CollectionAssert.Contains(typeof(IApprovalRequirementEvaluator).GetMethods().Select(method => method.Name).ToArray(), nameof(IApprovalRequirementEvaluator.Evaluate));
        Assert.AreEqual(1, typeof(IApprovalRequirementEvaluator).GetMethods().Length);
    }

    [TestMethod]
    public void ApprovalRequirementEvaluator_DoesNotExposeApproveExecuteOrContinueMethods()
    {
        var forbidden = new[] { "Approve", "Execute", "Continue", "Start", "Run", "Record", "CreateDecision", "LookupDecision" };
        var members = typeof(ApprovalRequirementEvaluator)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(method => method.Name)
            .ToArray();

        foreach (var member in members)
        {
            foreach (var token in forbidden)
            {
                Assert.IsFalse(member.Contains(token, StringComparison.OrdinalIgnoreCase), member);
            }
        }
    }

    [TestMethod]
    public void ApprovalRequirementEvaluationResult_AlwaysCarriesNegativeAuthorityFlags()
    {
        foreach (var result in new[]
        {
            _evaluator.Evaluate(ValidRequest()),
            _evaluator.Evaluate(ValidRequest() with { ApprovalRules = [] }),
            _evaluator.Evaluate(ValidRequest() with { ProjectId = Guid.Empty })
        })
        {
            AssertNoAuthority(result);
        }
    }

    [TestMethod]
    public void ApprovalRequirementOutcomeValues_AvoidAuthorityLanguage()
    {
        var forbidden = new[] { "Approved", "Authorized", "CanExecute", "ReadyToRun", "ExecutionAllowed", "PolicySatisfied", "PermissionGranted", "ReleaseReady", "CanShip", "ApplyAllowed", "PromotionAllowed" };

        foreach (var outcome in ApprovalRequirementOutcomes.All)
        {
            foreach (var token in forbidden)
            {
                Assert.IsFalse(outcome.Contains(token, StringComparison.OrdinalIgnoreCase), outcome);
            }
        }
    }

    [TestMethod]
    public void Evaluate_ReturnsApprovalRequiredForSingleApprovalRule()
    {
        var rule = ActiveRule(ProjectApprovalRuleApprovalTypes.Single, [ProjectApprovalRuleApproverTypes.ProjectLead]);
        var result = _evaluator.Evaluate(ValidRequest(rule));

        Assert.AreEqual(ApprovalRequirementOutcomes.ApprovalRequired, result.Outcome);
        Assert.AreEqual("MATCHING_RULE_REQUIRES_APPROVAL", result.ReasonCode);
        Assert.AreEqual(rule.ProjectApprovalRuleId, result.Requirements.Single().SourceRuleId);
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void Evaluate_ReturnsApprovalRequiredForHumanOnlyRule()
    {
        var result = _evaluator.Evaluate(ValidRequest(ActiveRule(ProjectApprovalRuleApprovalTypes.HumanOnly, [ProjectApprovalRuleApproverTypes.Human])));

        AssertRequirement(result, ApprovalRequirementOutcomes.ApprovalRequired, ProjectApprovalRuleApprovalTypes.HumanOnly);
    }

    [TestMethod]
    public void Evaluate_ReturnsApprovalRequiredForAllOfRule()
    {
        var result = _evaluator.Evaluate(ValidRequest(ActiveRule(ProjectApprovalRuleApprovalTypes.AllOf, [ProjectApprovalRuleApproverTypes.ProjectLead, ProjectApprovalRuleApproverTypes.SecurityOwner])));

        AssertRequirement(result, ApprovalRequirementOutcomes.ApprovalRequired, ProjectApprovalRuleApprovalTypes.AllOf);
    }

    [TestMethod]
    public void Evaluate_ReturnsApprovalRequiredForQuorumRule()
    {
        var rule = ActiveRule(ProjectApprovalRuleApprovalTypes.Quorum, [ProjectApprovalRuleApproverTypes.ProjectLead, ProjectApprovalRuleApproverTypes.SecurityOwner], quorumCount: 2);
        var result = _evaluator.Evaluate(ValidRequest(rule));

        AssertRequirement(result, ApprovalRequirementOutcomes.ApprovalRequired, ProjectApprovalRuleApprovalTypes.Quorum);
        Assert.AreEqual(2, result.Requirements.Single().QuorumCount);
    }

    [TestMethod]
    public void Evaluate_ReturnsNoApprovalRequiredOnlyForExplicitNonSensitiveNoneRule()
    {
        var result = _evaluator.Evaluate(ValidRequest());

        AssertRequirement(result, ApprovalRequirementOutcomes.NoApprovalRequired, ProjectApprovalRuleApprovalTypes.None);
        Assert.IsFalse(result.FailClosed);
    }

    [TestMethod]
    public void Evaluate_ReturnsNoMatchingRuleFailClosedWhenNoRuleMatches()
    {
        var result = _evaluator.Evaluate(ValidRequest() with { ActionName = "workspace.validate" });

        Assert.AreEqual(ApprovalRequirementOutcomes.NoMatchingRuleFailClosed, result.Outcome);
        Assert.IsTrue(result.FailClosed);
        Assert.IsFalse(result.Requirements.Any());
    }

    [TestMethod]
    public void Evaluate_ReturnsInvalidPolicyFailClosedForInactivePolicy()
    {
        var request = ValidRequest();
        var result = _evaluator.Evaluate(request with { ProjectPolicy = request.ProjectPolicy with { Status = nameof(ProjectAutonomyPolicyStatus.Draft) } });

        Assert.AreEqual(ApprovalRequirementOutcomes.InvalidPolicyFailClosed, result.Outcome);
        Assert.AreEqual("POLICY_NOT_ACTIVE", result.ReasonCode);
        Assert.IsTrue(result.FailClosed);
    }

    [TestMethod] public void SourceApply_RequiresHumanApproval() => AssertSensitiveRequiresHuman(ProjectApprovalRuleScopes.SourceApply);

    [TestMethod] public void MemoryPromotion_RequiresHumanApproval() => AssertSensitiveRequiresHuman(ProjectApprovalRuleScopes.MemoryPromotion);

    [TestMethod] public void ReleaseReadiness_RequiresHumanApproval() => AssertSensitiveRequiresHuman(ProjectApprovalRuleScopes.ReleaseReadiness);

    [TestMethod] public void ExternalSideEffect_RequiresHumanApproval() => AssertSensitiveRequiresHuman(ProjectApprovalRuleScopes.ExternalSideEffect);

    [TestMethod] public void DestructiveOperation_RequiresHumanApproval() => AssertSensitiveRequiresHuman(ProjectApprovalRuleScopes.DestructiveOperation);

    [TestMethod]
    public void SensitiveScope_RejectsApprovalTypeNone()
    {
        var rule = ActiveRule(ProjectApprovalRuleApprovalTypes.None, [], scope: ProjectApprovalRuleScopes.SourceApply, risk: ProjectApprovalRuleRiskLevels.Critical);
        var result = _evaluator.Evaluate(ValidRequest(rule) with { ApprovalScope = ProjectApprovalRuleScopes.SourceApply, ActionName = "source.apply" });

        Assert.AreEqual(ApprovalRequirementOutcomes.InvalidPolicyFailClosed, result.Outcome);
        Assert.AreEqual("RULE_INVALID", result.ReasonCode);
    }

    [TestMethod] public void SensitiveScope_RejectsAgentOnlyApprover() => AssertSensitiveRejectsAutomatedApprover(ProjectApprovalRuleApproverTypes.Agent);

    [TestMethod] public void SensitiveScope_RejectsSystemOnlyApprover() => AssertSensitiveRejectsAutomatedApprover(ProjectApprovalRuleApproverTypes.System);

    [TestMethod]
    public void SensitiveScope_MissingRuleFailsClosed()
    {
        var result = _evaluator.Evaluate(ValidRequest(ActiveRule(scope: ProjectApprovalRuleScopes.ToolExecution)) with { ApprovalScope = ProjectApprovalRuleScopes.SourceApply });

        Assert.AreEqual(ApprovalRequirementOutcomes.NoMatchingRuleFailClosed, result.Outcome);
        Assert.IsTrue(result.FailClosed);
    }

    [TestMethod]
    public void SensitiveScope_AmbiguousRulesFailClosed()
    {
        var first = ActiveRule(ProjectApprovalRuleApprovalTypes.HumanOnly, [ProjectApprovalRuleApproverTypes.Human], scope: ProjectApprovalRuleScopes.SourceApply, actionPattern: "source.apply", risk: ProjectApprovalRuleRiskLevels.Critical);
        var second = ActiveRule(ProjectApprovalRuleApprovalTypes.HumanOnly, [ProjectApprovalRuleApproverTypes.ProjectLead], scope: ProjectApprovalRuleScopes.SourceApply, actionPattern: "source.apply", risk: ProjectApprovalRuleRiskLevels.Critical);
        var result = _evaluator.Evaluate(ValidRequest(first, second) with { ApprovalScope = ProjectApprovalRuleScopes.SourceApply, ActionName = "source.apply" });

        Assert.AreEqual(ApprovalRequirementOutcomes.NoMatchingRuleFailClosed, result.Outcome);
        Assert.AreEqual("MATCHING_RULES_AMBIGUOUS_FAIL_CLOSED", result.ReasonCode);
        Assert.IsTrue(result.FailClosed);
    }

    [TestMethod] public void Conservative_MissingRuleFailsClosed() => AssertMissingRuleFailsClosed(nameof(ProjectAutonomyLevel.Conservative));

    [TestMethod] public void Balanced_MissingRuleFailsClosed() => AssertMissingRuleFailsClosed(nameof(ProjectAutonomyLevel.Balanced));

    [TestMethod] public void Experimental_MissingRuleStillFailsClosed() => AssertMissingRuleFailsClosed(nameof(ProjectAutonomyLevel.Experimental));

    [TestMethod]
    public void Experimental_DoesNotBypassSensitiveHumanApproval()
    {
        var policy = ActivePolicy(nameof(ProjectAutonomyLevel.Experimental));
        var rule = ActiveRule(ProjectApprovalRuleApprovalTypes.HumanOnly, [ProjectApprovalRuleApproverTypes.Human], scope: ProjectApprovalRuleScopes.MemoryPromotion, actionPattern: "memory.promote", risk: ProjectApprovalRuleRiskLevels.Critical);
        var result = _evaluator.Evaluate(ValidRequestForPolicy(policy, rule) with
        {
            ApprovalScope = ProjectApprovalRuleScopes.MemoryPromotion,
            ActionName = "memory.promote"
        });

        AssertRequirement(result, ApprovalRequirementOutcomes.ApprovalRequired, ProjectApprovalRuleApprovalTypes.HumanOnly);
    }

    [TestMethod]
    public void Experimental_AllowsNoApprovalOnlyForExplicitNonSensitiveRule()
    {
        var result = _evaluator.Evaluate(ValidRequestForPolicy(ActivePolicy(nameof(ProjectAutonomyLevel.Experimental))));

        AssertRequirement(result, ApprovalRequirementOutcomes.NoApprovalRequired, ProjectApprovalRuleApprovalTypes.None);
    }

    [TestMethod]
    public void AutonomyLevel_DoesNotGrantExecution()
    {
        var result = _evaluator.Evaluate(ValidRequestForPolicy(ActivePolicy(nameof(ProjectAutonomyLevel.Experimental))));

        AssertNoAuthority(result);
        Assert.IsFalse(result.GrantsExecution);
    }

    [TestMethod]
    public void Evaluate_MatchesExactSubjectAndActionFirst()
    {
        var wildcard = ActiveRule(ProjectApprovalRuleApprovalTypes.HumanOnly, [ProjectApprovalRuleApproverTypes.Human], subjectPattern: "*", actionPattern: "*");
        var exact = ActiveRule(ProjectApprovalRuleApprovalTypes.None, [], subjectPattern: "tool_request", actionPattern: "workspace.diff");
        var result = _evaluator.Evaluate(ValidRequest(wildcard, exact));

        Assert.AreEqual(ApprovalRequirementOutcomes.NoApprovalRequired, result.Outcome);
        Assert.AreEqual(exact.ProjectApprovalRuleId, result.Requirements.Single().SourceRuleId);
    }

    [TestMethod]
    public void Evaluate_MatchesWildcardSubjectWhenNoExactSubject()
    {
        var rule = ActiveRule(ProjectApprovalRuleApprovalTypes.Single, [ProjectApprovalRuleApproverTypes.Operator], subjectPattern: "*", actionPattern: "workspace.diff");
        var result = _evaluator.Evaluate(ValidRequest(rule));

        AssertRequirement(result, ApprovalRequirementOutcomes.ApprovalRequired, ProjectApprovalRuleApprovalTypes.Single);
    }

    [TestMethod]
    public void Evaluate_MatchesWildcardActionWhenNoExactAction()
    {
        var rule = ActiveRule(ProjectApprovalRuleApprovalTypes.Single, [ProjectApprovalRuleApproverTypes.Operator], subjectPattern: "tool_request", actionPattern: "*");
        var result = _evaluator.Evaluate(ValidRequest(rule));

        AssertRequirement(result, ApprovalRequirementOutcomes.ApprovalRequired, ProjectApprovalRuleApprovalTypes.Single);
    }

    [TestMethod]
    public void Evaluate_UsesMostRestrictiveRuleWhenMultipleMatch()
    {
        var single = ActiveRule(ProjectApprovalRuleApprovalTypes.Single, [ProjectApprovalRuleApproverTypes.Operator], actionPattern: "workspace.diff");
        var humanOnly = ActiveRule(ProjectApprovalRuleApprovalTypes.HumanOnly, [ProjectApprovalRuleApproverTypes.Human], actionPattern: "workspace.diff");
        var result = _evaluator.Evaluate(ValidRequest(single, humanOnly));

        AssertRequirement(result, ApprovalRequirementOutcomes.ApprovalRequired, ProjectApprovalRuleApprovalTypes.HumanOnly);
    }

    [TestMethod]
    public void Evaluate_FailsClosedWhenRulePrecedenceAmbiguous()
    {
        var first = ActiveRule(ProjectApprovalRuleApprovalTypes.Single, [ProjectApprovalRuleApproverTypes.Operator], actionPattern: "workspace.diff", risk: ProjectApprovalRuleRiskLevels.High);
        var second = ActiveRule(ProjectApprovalRuleApprovalTypes.Single, [ProjectApprovalRuleApproverTypes.ProjectLead], actionPattern: "workspace.diff", risk: ProjectApprovalRuleRiskLevels.High);
        var result = _evaluator.Evaluate(ValidRequest(first, second));

        Assert.AreEqual(ApprovalRequirementOutcomes.NoMatchingRuleFailClosed, result.Outcome);
        Assert.AreEqual("MATCHING_RULES_AMBIGUOUS_FAIL_CLOSED", result.ReasonCode);
    }

    [TestMethod]
    public void Evaluate_DoesNotUseSemanticMatching()
    {
        var rule = ActiveRule(actionPattern: "workspace.validate");
        var result = _evaluator.Evaluate(ValidRequest(rule) with { ActionName = "run a build check" });

        Assert.AreEqual(ApprovalRequirementOutcomes.NoMatchingRuleFailClosed, result.Outcome);
    }

    [TestMethod]
    public void Evaluate_DoesNotUseVectorMatching() => AssertSourceDoesNotContain("Vector", "Embedding", "Retrieval", "Similarity");

    [TestMethod]
    public void Evaluate_DoesNotUseModelInterpretation() => AssertSourceDoesNotContain("LLM", "ModelAdapter", "Semantic");

    [TestMethod]
    public void Evaluate_RejectsEmptyProjectId()
    {
        var result = _evaluator.Evaluate(ValidRequest() with { ProjectId = Guid.Empty });

        AssertInvalidRequest(result, "PROJECT_ID_REQUIRED");
    }

    [TestMethod]
    public void Evaluate_RejectsBlankApprovalScope()
    {
        var result = _evaluator.Evaluate(ValidRequest() with { ApprovalScope = " " });

        AssertInvalidRequest(result, "APPROVAL_SCOPE_REQUIRED");
    }

    [TestMethod]
    public void Evaluate_RejectsUnknownApprovalScope()
    {
        var result = _evaluator.Evaluate(ValidRequest() with { ApprovalScope = "ship_it" });

        AssertInvalidRequest(result, "APPROVAL_SCOPE_UNKNOWN");
    }

    [TestMethod]
    public void Evaluate_RejectsBlankSubjectType()
    {
        var result = _evaluator.Evaluate(ValidRequest() with { SubjectType = " " });

        AssertInvalidRequest(result, "SUBJECT_TYPE_REQUIRED");
    }

    [TestMethod]
    public void Evaluate_RejectsBlankSubjectId()
    {
        var result = _evaluator.Evaluate(ValidRequest() with { SubjectId = " " });

        AssertInvalidRequest(result, "SUBJECT_ID_REQUIRED");
    }

    [TestMethod]
    public void Evaluate_RejectsBlankActorType()
    {
        var result = _evaluator.Evaluate(ValidRequest() with { RequestedByActorType = " " });

        AssertInvalidRequest(result, "REQUESTED_BY_ACTOR_TYPE_REQUIRED");
    }

    [TestMethod]
    public void Evaluate_RejectsBlankActorId()
    {
        var result = _evaluator.Evaluate(ValidRequest() with { RequestedByActorId = " " });

        AssertInvalidRequest(result, "REQUESTED_BY_ACTOR_ID_REQUIRED");
    }

    [TestMethod]
    public void Evaluate_RejectsInvalidContextJson()
    {
        var result = _evaluator.Evaluate(ValidRequest() with { ContextJson = "{bad-json" });

        AssertInvalidRequest(result, "CONTEXT_JSON_INVALID");
    }

    [TestMethod]
    public void Evaluate_RejectsPrivateReasoningMarkers()
    {
        var result = _evaluator.Evaluate(ValidRequest() with
        {
            ContextJson = """
                {
                  "schema": "approval.requirement.context.v1",
                  "hiddenReasoning": "chain-of-thought"
                }
                """
        });

        AssertInvalidRequest(result, "CONTEXT_PRIVATE_REASONING");
    }

    [TestMethod]
    public void Evaluate_RejectsPolicyFromDifferentProject()
    {
        var result = _evaluator.Evaluate(ValidRequest() with { ProjectPolicy = ActivePolicy() with { ProjectId = Guid.NewGuid() } });

        AssertInvalidPolicy(result, "POLICY_PROJECT_MISMATCH");
    }

    [TestMethod]
    public void Evaluate_RejectsRuleFromDifferentProject()
    {
        var request = ValidRequest();
        var result = _evaluator.Evaluate(request with { ApprovalRules = [request.ApprovalRules.Single() with { ProjectId = Guid.NewGuid() }] });

        AssertInvalidPolicy(result, "RULE_PROJECT_MISMATCH");
    }

    [TestMethod]
    public void Evaluate_RejectsRuleForDifferentPolicy()
    {
        var request = ValidRequest();
        var result = _evaluator.Evaluate(request with { ApprovalRules = [request.ApprovalRules.Single() with { ProjectAutonomyPolicyId = Guid.NewGuid() }] });

        AssertInvalidPolicy(result, "RULE_POLICY_MISMATCH");
    }

    [TestMethod]
    public void Evaluate_RejectsInvalidApprovalRule()
    {
        var result = _evaluator.Evaluate(ValidRequest(ActiveRule() with { RuleName = " " }));

        AssertInvalidPolicy(result, "RULE_INVALID");
    }

    [TestMethod] public void Evaluate_DoesNotCreateApprovalDecision() => AssertSourceDoesNotContain("IApprovalDecisionStore", "ApprovalDecisionStore", "approval.decision.recorded");

    [TestMethod] public void Evaluate_DoesNotCreatePolicyDecisionEvent() => AssertSourceDoesNotContain("IPolicyDecisionEventStore", "PolicyDecisionEventStore", "policy.decision.recorded");

    [TestMethod] public void Evaluate_DoesNotCreateDogfoodReceipt() => AssertSourceDoesNotContain("IDogfoodReceiptStore", "DogfoodReceiptStore", "dogfood.receipt.recorded");

    [TestMethod] public void Evaluate_DoesNotExecuteTool() => AssertSourceDoesNotContain("IToolExecutor", "ToolExecutionAuditRecordFactory", "Process.Start");

    [TestMethod] public void Evaluate_DoesNotStartWorkflow() => AssertSourceDoesNotContain("WorkflowRunner", "StartWorkflow", "ContinueWorkflow");

    [TestMethod] public void Evaluate_DoesNotMutateSource() => AssertSourceDoesNotContain("IWorkspaceApply", "File.Copy", "File.Delete", "sourceRepoMutated");

    [TestMethod] public void Evaluate_DoesNotPromoteMemory() => AssertSourceDoesNotContain("CollectiveMemoryPromotion", "PromoteCollectiveMemory", "AcceptedMemory");

    [TestMethod] public void Evaluate_DoesNotCreateA2aHandoff() => AssertSourceDoesNotContain("IA2a", "A2aMessageBus", "CreateA2aHandoff");

    [TestMethod] public void Evaluate_DoesNotMarkReleaseReady() => AssertSourceDoesNotContain("ReleaseReady", "CanShip", "ReadyToShip");

    [TestMethod]
    public void Evaluate_DoesNotTransferAuthority()
    {
        var result = _evaluator.Evaluate(ValidRequest());

        Assert.IsFalse(result.TransfersAuthority);
        Assert.IsFalse(result.SatisfiesPolicy);
        Assert.IsFalse(result.GrantsApproval);
    }

    [TestMethod]
    public void ApprovalRequirementEvaluator_IsNotRegisteredInApiDi()
    {
        var program = File.ReadAllText(Path.Combine(RepositoryRoot(), "IronDev.Api", "Program.cs"));

        Assert.IsFalse(program.Contains("ApprovalRequirementEvaluator", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(program.Contains("IApprovalRequirementEvaluator", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod] public void ApprovalRequirementEvaluator_DoesNotAddApiEndpoint() => AssertDirectoryDoesNotContain("IronDev.Api", "approval-requirements", "ApprovalRequirementEvaluator");

    [TestMethod] public void ApprovalRequirementEvaluator_DoesNotAddCliCommand() => AssertDirectoryDoesNotContain(Path.Combine("tools", "IronDev.Cli"), "approval-requirement", "ApprovalRequirementEvaluator");

    [TestMethod]
    public void ApprovalRequirementEvaluator_DoesNotAddSqlMigration()
    {
        var databaseRoot = Path.Combine(RepositoryRoot(), "Database");
        if (!Directory.Exists(databaseRoot))
        {
            return;
        }

        var sqlText = string.Join(Environment.NewLine, Directory.EnumerateFiles(databaseRoot, "*.sql", SearchOption.AllDirectories).Select(File.ReadAllText));

        Assert.IsFalse(sqlText.Contains("approval_requirement", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(sqlText.Contains("ApprovalRequirementEvaluator", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod] public void ApprovalRequirementEvaluator_DoesNotReferenceWorkflowRunner() => AssertSourceDoesNotContain("WorkflowRunner", "IWorkflowRunner", "ContinueWorkflow");

    [TestMethod] public void ApprovalRequirementEvaluator_DoesNotReferenceExecutor() => AssertSourceDoesNotContain("ExecutorService", "IToolExecutor", "IAgentToolExecutor");

    [TestMethod] public void ApprovalRequirementEvaluator_DoesNotReferenceMemoryPromotion() => AssertSourceDoesNotContain("MemoryPromotionService", "PromoteMemory", "CollectiveMemoryPromotion");

    [TestMethod] public void ApprovalRequirementEvaluator_DoesNotReferenceSourceApply() => AssertSourceDoesNotContain("ApplyCopy", "SourceApplyService", "IWorkspaceApplyCopy");

    [TestMethod] public void ApprovalRequirementEvaluator_DoesNotReferenceLangGraph() => AssertSourceDoesNotContain("LangGraph");

    [TestMethod] public void ApprovalRequirementEvaluator_DoesNotReferenceA2a() => AssertSourceDoesNotContain("A2aMessageBus", "IA2a");

    [TestMethod]
    public void ApprovalRequirementEvaluator_Documentation_StatesBoundary()
    {
        var doc = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "BLOCK_H_PROJECT_AUTHORITY_POLICY_MODEL.md"));

        StringAssert.Contains(doc, "PR84 adds deterministic requirement evaluation.");
        StringAssert.Contains(doc, "It returns approval requirements only.");
        StringAssert.Contains(doc, "PR84 does not check existing approval decisions.");
        StringAssert.Contains(doc, "PR84 does not create approval decisions.");
        StringAssert.Contains(doc, "PR84 does not create policy decision events.");
        StringAssert.Contains(doc, "PR84 does not execute anything.");
        StringAssert.Contains(doc, "PR84 does not start or continue workflow.");
        StringAssert.Contains(doc, "PR84 does not expose API or CLI endpoints.");
        StringAssert.Contains(doc, "Missing policy or rules fail closed.");
        StringAssert.Contains(doc, "Sensitive scopes require human approval.");
        StringAssert.Contains(doc, "Experimental autonomy does not bypass sensitive approval.");
    }

    private static void AssertSensitiveRequiresHuman(string scope)
    {
        var rule = ActiveRule(ProjectApprovalRuleApprovalTypes.HumanOnly, [ProjectApprovalRuleApproverTypes.Human], scope: scope, actionPattern: "sensitive.action", risk: ProjectApprovalRuleRiskLevels.Critical);
        var result = new ApprovalRequirementEvaluator().Evaluate(ValidRequest(rule) with { ApprovalScope = scope, ActionName = "sensitive.action" });

        AssertRequirement(result, ApprovalRequirementOutcomes.ApprovalRequired, ProjectApprovalRuleApprovalTypes.HumanOnly);
        CollectionAssert.Contains(result.Requirements.Single().RequiredApproverTypes.ToArray(), ProjectApprovalRuleApproverTypes.Human);
    }

    private static void AssertSensitiveRejectsAutomatedApprover(string approverType)
    {
        var rule = ActiveRule(ProjectApprovalRuleApprovalTypes.Single, [approverType], scope: ProjectApprovalRuleScopes.SourceApply, actionPattern: "source.apply", risk: ProjectApprovalRuleRiskLevels.Critical);
        var result = new ApprovalRequirementEvaluator().Evaluate(ValidRequest(rule) with { ApprovalScope = ProjectApprovalRuleScopes.SourceApply, ActionName = "source.apply" });

        AssertInvalidPolicy(result, "RULE_INVALID");
    }

    private static void AssertMissingRuleFailsClosed(string autonomyLevel)
    {
        var policy = ActivePolicy(autonomyLevel);
        var result = new ApprovalRequirementEvaluator().Evaluate(ValidRequestForPolicy(policy) with
        {
            ActionName = "workspace.validate"
        });

        Assert.AreEqual(ApprovalRequirementOutcomes.NoMatchingRuleFailClosed, result.Outcome);
        Assert.IsTrue(result.FailClosed);
        AssertNoAuthority(result);
    }

    private static void AssertRequirement(ApprovalRequirementEvaluationResult result, string outcome, string approvalType)
    {
        Assert.AreEqual(outcome, result.Outcome);
        Assert.AreEqual(1, result.Requirements.Count);
        Assert.AreEqual(approvalType, result.Requirements.Single().ApprovalType);
        AssertNoAuthority(result);
    }

    private static void AssertInvalidRequest(ApprovalRequirementEvaluationResult result, string reasonCode)
    {
        Assert.AreEqual(ApprovalRequirementOutcomes.InvalidRequest, result.Outcome);
        Assert.AreEqual(reasonCode, result.ReasonCode);
        Assert.IsTrue(result.FailClosed);
        AssertNoAuthority(result);
    }

    private static void AssertInvalidPolicy(ApprovalRequirementEvaluationResult result, string reasonCode)
    {
        Assert.AreEqual(ApprovalRequirementOutcomes.InvalidPolicyFailClosed, result.Outcome);
        Assert.AreEqual(reasonCode, result.ReasonCode);
        Assert.IsTrue(result.FailClosed);
        AssertNoAuthority(result);
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
        ValidRequestForPolicy(ActivePolicy(), rules);

    private static ApprovalRequirementEvaluationRequest ValidRequestForPolicy(ProjectAutonomyPolicy policy, params ProjectApprovalRule[] rules)
    {
        var ruleSet = rules.Length == 0
            ? [ActiveRule(policyId: policy.ProjectAutonomyPolicyId) with { ProjectId = policy.ProjectId, ProjectAutonomyPolicyId = policy.ProjectAutonomyPolicyId }]
            : rules.Select(rule => rule with { ProjectId = policy.ProjectId, ProjectAutonomyPolicyId = policy.ProjectAutonomyPolicyId }).ToArray();

        return new ApprovalRequirementEvaluationRequest
        {
            ProjectId = policy.ProjectId,
            ProjectPolicy = policy,
            ApprovalRules = ruleSet,
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
    }

    private static ProjectAutonomyPolicy ActivePolicy(string autonomyLevel = nameof(ProjectAutonomyLevel.Balanced)) =>
        new()
        {
            ProjectAutonomyPolicyId = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            PolicyName = "Backend governance policy",
            PolicyVersion = 1,
            AutonomyLevel = autonomyLevel,
            Status = nameof(ProjectAutonomyPolicyStatus.Active),
            CreatedByActorType = "human",
            CreatedByActorId = "human-reviewer",
            MetadataVersion = 1,
            MetadataJson = """
                {
                  "schema": "project.autonomy.policy.metadata.v1",
                  "schemaVersion": 1,
                  "notes": "Active policy for evaluator tests."
                }
                """,
            CreatedUtc = DateTimeOffset.UtcNow
        };

    private static ProjectApprovalRule ActiveRule(
        string approvalType = ProjectApprovalRuleApprovalTypes.None,
        IReadOnlyList<string>? approverTypes = null,
        string scope = ProjectApprovalRuleScopes.ToolExecution,
        string subjectPattern = "tool_request",
        string actionPattern = "workspace.diff",
        string risk = ProjectApprovalRuleRiskLevels.Low,
        Guid? policyId = null,
        int? quorumCount = null) =>
        new()
        {
            ProjectApprovalRuleId = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            ProjectAutonomyPolicyId = policyId ?? Guid.NewGuid(),
            RuleName = $"Rule {Guid.NewGuid():N}",
            RuleVersion = 1,
            Status = ProjectApprovalRuleStatuses.Active,
            ApprovalScope = scope,
            SubjectTypePattern = subjectPattern,
            ActionNamePattern = actionPattern,
            RiskLevel = risk,
            ApprovalType = approvalType,
            ApproverTypes = approverTypes ?? [],
            QuorumCount = quorumCount,
            CreatedByActorType = "human",
            CreatedByActorId = "human-reviewer",
            MetadataVersion = 1,
            MetadataJson = """
                {
                  "schema": "project.approval.rule.metadata.v1",
                  "notes": "Rule contract metadata for evaluator tests.",
                  "grantsApproval": false,
                  "grantsExecution": false,
                  "mutatesSource": false,
                  "promotesMemory": false,
                  "startsWorkflow": false,
                  "satisfiesPolicy": false,
                  "transfersAuthority": false
                }
                """,
            CreatedUtc = DateTimeOffset.UtcNow
        };

    private static void AssertSourceDoesNotContain(params string[] tokens)
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "IronDev.Core", "Policy", "ApprovalRequirementEvaluatorModels.cs"));

        foreach (var token in tokens)
        {
            Assert.IsFalse(source.Contains(token, StringComparison.OrdinalIgnoreCase), token);
        }
    }

    private static void AssertDirectoryDoesNotContain(string relativeDirectory, params string[] tokens)
    {
        var directory = Path.Combine(RepositoryRoot(), relativeDirectory);
        if (!Directory.Exists(directory))
        {
            return;
        }

        var text = string.Join(Environment.NewLine, Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));
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
