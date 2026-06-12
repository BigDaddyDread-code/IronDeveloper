using IronDev.Core.Agents;
using IronDev.Core.Agents.Audit;
using IronDev.Core.Agents.Concrete;
using IronDev.Core.Governance;
using IronDev.Core.Policy;
using IronDev.Core.RunReports;

namespace IronDev.IntegrationTests.Policy;

[TestClass]
[TestCategory("ApprovalAuthorityBoundary")]
public sealed class ApprovalAuthorityBoundaryTests
{
    private static readonly Guid ProjectId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PolicyId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset FixedUtc = new(2026, 6, 12, 0, 0, 0, TimeSpan.Zero);

    [TestMethod] public void ToolGateDecisionPassed_IsNotApproval() => AssertNoApproval(Facts("tool_gate_decision", "passed"));

    [TestMethod]
    public void ToolGateDecisionPassed_IsNotExecutionPermission()
    {
        var decision = AllowedGateDecision();

        Assert.IsTrue(decision.RequiresExecutor);
        AssertNoActiveEffects(decision);
    }

    [TestMethod] public void ToolGateDecisionRequiresApproval_IsNotApproval() => AssertNoApproval(RequiresApprovalGateDecision());
    [TestMethod] public void ToolGateDecisionBlocked_IsNotApproval() => AssertNoApproval(BlockedGateDecision());
    [TestMethod] public void ToolGateDecision_DoesNotCreateApprovalDecision() => AssertNoApproval(Facts("tool_gate_decision", "passed", createsApprovalDecision: false));
    [TestMethod] public void ToolGateDecision_DoesNotSatisfyApprovalRequirement() => AssertNoApproval(Facts("tool_gate_decision", "passed", satisfiesApprovalRequirement: false));
    [TestMethod] public void ToolGateDecision_DoesNotSatisfyPolicy() => AssertNoApproval(Facts("tool_gate_decision", "passed", satisfiesPolicy: false));

    [TestMethod] public void PolicyDecisionNoPolicyBlock_IsNotApproval() => AssertNoApproval(Facts("policy_decision_event", nameof(PolicyDecisionValue.NoPolicyBlock)));

    [TestMethod]
    public void PolicyDecisionNoPolicyBlock_IsNotPermission()
    {
        var names = Enum.GetNames<PolicyDecisionValue>();

        CollectionAssert.Contains(names, nameof(PolicyDecisionValue.NoPolicyBlock));
        CollectionAssert.DoesNotContain(names, "Allowed");
        CollectionAssert.DoesNotContain(names, "Approved");
        CollectionAssert.DoesNotContain(names, "PolicySatisfied");
    }

    [TestMethod] public void PolicyDecisionRequiresApproval_IsNotApproval() => AssertNoApproval(Facts("policy_decision_event", nameof(PolicyDecisionValue.RequiresApproval)));
    [TestMethod] public void PolicyDecisionBlocked_IsNotApproval() => AssertNoApproval(Facts("policy_decision_event", nameof(PolicyDecisionValue.Blocked)));
    [TestMethod] public void PolicyDecision_DoesNotCreateApprovalDecision() => AssertNoApproval(Facts("policy_decision_event", "recorded", createsApprovalDecision: false));
    [TestMethod] public void PolicyDecision_DoesNotSatisfyApprovalRequirement() => AssertNoApproval(Facts("policy_decision_event", "recorded", satisfiesApprovalRequirement: false));
    [TestMethod] public void PolicyDecision_DoesNotAllowExecution() => AssertNoApproval(Facts("policy_decision_event", "recorded", grantsExecution: false));

    [TestMethod]
    public void PolicyDecisionUnsafeEvidenceFlags_AreRejected()
    {
        var result = new PolicyDecisionValidator().ValidateRecord(PolicyDecisionRecord() with
        {
            EvidenceJson = "{\"schema\":\"policy.decision.evidence.v1\",\"grantsApproval\":true}"
        });

        AssertHasIssue(result.Issues, "EVIDENCE_APPROVAL_FORBIDDEN");
    }

    [TestMethod]
    public void PolicyDecisionNoPolicyBlock_WordingIsRecordedAsNotApproval()
    {
        const string meaning = "NoPolicyBlock means no policy block was recorded. It does not mean approved. It does not mean allowed. It does not mean ready to run.";

        StringAssert.Contains(meaning, "does not mean approved");
        StringAssert.Contains(meaning, "does not mean allowed");
        StringAssert.Contains(meaning, "does not mean ready to run");
    }

    [TestMethod] public void DogfoodReceiptPassed_IsNotApproval() => AssertNoApproval(Facts("dogfood_receipt", "passed"));
    [TestMethod] public void DogfoodReceiptPassed_IsNotReleaseApproval() => AssertNoApproval(Facts("dogfood_receipt", "passed", approvesRelease: false));
    [TestMethod] public void DogfoodReceiptPassed_IsNotPolicySatisfied() => AssertNoApproval(Facts("dogfood_receipt", "passed", satisfiesPolicy: false));
    [TestMethod] public void DogfoodReceiptPassed_DoesNotSatisfyApprovalRequirement() => AssertNoApproval(Facts("dogfood_receipt", "passed", satisfiesApprovalRequirement: false));
    [TestMethod] public void DogfoodReceiptPassed_DoesNotContinueWorkflow() => AssertNoApproval(Facts("dogfood_receipt", "passed", startsWorkflow: false));
    [TestMethod] public void DogfoodReceiptPassed_DoesNotAllowSourceApply() => AssertNoApproval(Facts("dogfood_receipt", "passed", mutatesSource: false));
    [TestMethod] public void DogfoodReceiptPassed_DoesNotPromoteMemory() => AssertNoApproval(Facts("dogfood_receipt", "passed", promotesMemory: false));

    [TestMethod]
    public void DogfoodReceiptForbiddenOutcomeNames_AreRejected()
    {
        var result = new DogfoodReceiptValidator().ValidateRecord(DogfoodReceiptRecord() with { Outcome = "ReleaseApproved" });

        AssertHasIssue(result.Issues, "OUTCOME_INVALID");
        AssertHasIssue(result.Issues, "OUTCOME_AUTHORITY_LANGUAGE_FORBIDDEN");
    }

    [TestMethod]
    public void DogfoodReceiptUnsafeEvidenceFlags_AreRejected()
    {
        var result = new DogfoodReceiptValidator().ValidateRecord(DogfoodReceiptRecord() with
        {
            EvidenceJson = "{\"schema\":\"dogfood.receipt.evidence.v1\",\"grantsApproval\":true,\"approvesRelease\":true}"
        });

        AssertHasIssue(result.Issues, "EVIDENCE_APPROVAL_FORBIDDEN");
        AssertHasIssue(result.Issues, "EVIDENCE_RELEASE_APPROVAL_FORBIDDEN");
    }

    [TestMethod] public void CriticReviewAccepted_IsNotApproval() => AssertCriticIsNotApproval(CriticReviewVerdict.NoObjection);
    [TestMethod] public void CriticReviewClean_IsNotApproval() => AssertCriticIsNotApproval(CriticReviewVerdict.NoObjection);
    [TestMethod] public void CriticReviewNoIssues_IsNotApproval() => AssertCriticIsNotApproval(CriticReviewVerdict.NoObjection);
    [TestMethod] public void CodeStandardsClean_IsNotApproval() => AssertNoApproval(Facts("code_standards_review", "clean"));
    [TestMethod] public void CodeStandardsClean_DoesNotSatisfyApprovalRequirement() => AssertNoApproval(Facts("code_standards_review", "clean", satisfiesApprovalRequirement: false));
    [TestMethod] public void CriticOutput_DoesNotCreateApprovalDecision() => AssertNoApproval(Facts("critic_review", "no_objection", createsApprovalDecision: false));
    [TestMethod] public void CriticOutput_DoesNotAllowExecution() => AssertNoApproval(Facts("critic_review", "no_objection", grantsExecution: false));

    [TestMethod]
    public void CriticOutput_AuthorityClaimsAreRejected()
    {
        var result = SafeCriticReview() with
        {
            Findings = [SafeFinding() with { RequiredFix = "Approved by critic. Ready to run." }]
        };

        AssertHasIssue(new CriticReviewResultValidator().Validate(result), CriticReviewResultValidator.AuthorityClaimBlocked);
    }

    [TestMethod] public void ApprovalRequirementEvaluation_ApprovalRequired_IsNotApproval() => AssertNoApproval(ApprovalRequiredEvaluation());
    [TestMethod] public void ApprovalRequirementEvaluation_NoApprovalRequired_IsNotApproval() => AssertNoApproval(NoApprovalRequiredEvaluation());
    [TestMethod] public void ApprovalRequirementEvaluation_NoApprovalRequired_IsNotExecutionPermission() => AssertNoApproval(NoApprovalRequiredEvaluation());

    [TestMethod]
    public void ApprovalRequirementEvaluation_DoesNotCheckApprovalDecision()
    {
        var methods = typeof(IApprovalRequirementEvaluator).GetMethods().Select(method => method.Name).ToArray();

        CollectionAssert.AreEquivalent(new[] { nameof(IApprovalRequirementEvaluator.Evaluate) }, methods);
        AssertFileDoesNotContain(Path.Combine("IronDev.Core", "Policy", "ApprovalRequirementEvaluatorModels.cs"), "ApprovalDecisionStore", "IApprovalDecisionStore", "ApprovalDecisionRecord");
    }

    [TestMethod] public void ApprovalRequirementEvaluation_DoesNotCreateApprovalDecision() => AssertNoApproval(Facts("approval_requirement_evaluation", "approval_required", createsApprovalDecision: false));
    [TestMethod] public void ApprovalRequirementEvaluation_DoesNotSatisfyPolicy() => AssertNoApproval(ApprovalRequiredEvaluation());
    [TestMethod] public void ApprovalRequirementEvaluation_DoesNotExecuteTool() => AssertNoApproval(ApprovalRequiredEvaluation());

    [TestMethod] public void ApprovalPackageDraft_IsNotApproval() => AssertNoApproval(ReadyForReviewPackage() with { Status = ApprovalPackageStatuses.Draft });
    [TestMethod] public void ApprovalPackageReadyForReview_IsNotApproval() => AssertNoApproval(ReadyForReviewPackage());
    [TestMethod] public void ApprovalPackageReadyForReview_IsNotPolicySatisfied() => Assert.IsFalse(ReadyForReviewPackage().SatisfiesPolicy);
    [TestMethod] public void ApprovalPackageReadyForReview_DoesNotSatisfyRequirement() => AssertNoApproval(ReadyForReviewPackage() with { SatisfiesPolicy = false });
    [TestMethod] public void ApprovalPackageReadyForReview_DoesNotExecuteTool() => Assert.IsFalse(ReadyForReviewPackage().GrantsExecution);
    [TestMethod] public void ApprovalPackageReadyForReview_DoesNotAllowSourceApply() => Assert.IsFalse(ReadyForReviewPackage().MutatesSource);
    [TestMethod] public void ApprovalPackageReadyForReview_DoesNotPromoteMemory() => Assert.IsFalse(ReadyForReviewPackage().PromotesMemory);

    [TestMethod]
    public void ApprovalPackageForbiddenStatusNames_AreRejected() =>
        AssertHasIssue(ApprovalPackageValidator.Validate(ReadyForReviewPackage() with { Status = "Approved" }).Issues, "STATUS_FORBIDDEN");

    [TestMethod]
    public void ApprovalPackageForbiddenEvidenceTypes_AreRejected()
    {
        var package = ReadyForReviewPackage() with
        {
            EvidenceReferences = [new ApprovalPackageEvidenceReference { EvidenceType = "ApprovalDecisionSatisfied", EvidenceId = "approval-evidence-1" }]
        };

        AssertHasIssue(ApprovalPackageValidator.Validate(package).Issues, "EVIDENCE_TYPE_FORBIDDEN");
    }

    [TestMethod] public void ConservativeProfile_IsNotActivePolicy() => AssertProfileIsNotAuthority(ProjectPolicyProfileNames.Conservative);
    [TestMethod] public void BalancedProfile_IsNotActivePolicy() => AssertProfileIsNotAuthority(ProjectPolicyProfileNames.Balanced);
    [TestMethod] public void ExperimentalProfile_IsNotActivePolicy() => AssertProfileIsNotAuthority(ProjectPolicyProfileNames.Experimental);
    [TestMethod] public void ExperimentalProfile_IsNotPermission() => AssertNoApproval(ProfileFacts(ProjectPolicyProfileNames.Experimental));

    [TestMethod]
    public void ExperimentalProfile_DoesNotBypassSensitiveApproval()
    {
        var sensitive = ProjectPolicyProfileCatalog.Get(ProjectPolicyProfileNames.Experimental)
            .RuleTemplates
            .Where(rule => ProjectApprovalRuleScopes.IsSensitive(rule.ApprovalScope))
            .ToArray();

        Assert.IsTrue(sensitive.Length > 0);
        Assert.IsTrue(sensitive.All(rule => !string.Equals(rule.ApprovalType, ProjectApprovalRuleApprovalTypes.None, StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(sensitive.All(rule => rule.ApproverTypes.Any(ProjectApprovalRuleApproverTypes.IsHumanClass)));
    }

    [TestMethod]
    public void PolicyProfileFactory_DoesNotActivatePolicy() =>
        Assert.AreEqual(nameof(ProjectAutonomyPolicyStatus.Draft), new ProjectPolicyProfileFactory().CreateDraftPolicy(ProjectId, ProjectPolicyProfileNames.Experimental, "human", "reviewer").Status);

    [TestMethod]
    public void PolicyProfileFactory_DoesNotEvaluatePolicy()
    {
        var methods = typeof(IProjectPolicyProfileFactory).GetMethods().Select(method => method.Name).ToArray();

        CollectionAssert.AreEquivalent(new[] { nameof(IProjectPolicyProfileFactory.CreateDraftPolicy), nameof(IProjectPolicyProfileFactory.CreateDraftApprovalRules) }, methods);
    }

    [TestMethod]
    public void PolicyProfileFactory_DoesNotCreateApprovalDecision() =>
        Assert.IsFalse(typeof(IProjectPolicyProfileFactory).GetMethods().Any(method => method.Name.Contains("ApprovalDecision", StringComparison.OrdinalIgnoreCase) || method.Name.Contains("Approve", StringComparison.OrdinalIgnoreCase)));

    [TestMethod] public void GovernanceEvent_IsNotApproval() => AssertNoApproval(Facts("governance_event", "recorded"));
    [TestMethod] public void GovernanceEvent_DoesNotSatisfyApprovalRequirement() => AssertNoApproval(Facts("governance_event", "recorded", satisfiesApprovalRequirement: false));
    [TestMethod] public void ThoughtLedgerGovernanceReference_IsNotApproval() => AssertNoApproval(ThoughtLedgerFacts());
    [TestMethod] public void ThoughtLedgerGovernanceReference_DoesNotTransferAuthority() => AssertNoApproval(ThoughtLedgerFacts() with { TransfersAuthority = false });
    [TestMethod] public void ThoughtLedgerGovernanceReference_DoesNotSatisfyPolicy() => AssertNoApproval(ThoughtLedgerFacts() with { SatisfiesPolicy = false });
    [TestMethod] public void ThoughtLedgerGovernanceReference_DoesNotCreateApprovalDecision() => AssertNoApproval(ThoughtLedgerFacts() with { CreatesApprovalDecision = false });

    [TestMethod]
    public void ThoughtLedgerEntryAuthorityFlags_AreFalseForSafeReferences()
    {
        var entry = SafeThoughtLedgerEntry();

        Assert.IsFalse(entry.GrantsAuthority);
        Assert.IsFalse(entry.GrantsApproval);
        Assert.IsFalse(entry.GrantsMemoryPromotion);
    }

    [TestMethod] public void ValidationPassed_IsNotApproval() => AssertNoApproval(Facts("validation_output", "passed"));
    [TestMethod] public void BuildPassed_IsNotApproval() => AssertNoApproval(Facts("build_output", "passed"));
    [TestMethod] public void TestPassed_IsNotApproval() => AssertNoApproval(Facts("test_output", "passed"));
    [TestMethod] public void RunReportSuccessful_IsNotApproval() => AssertNoApproval(SuccessfulRunReportFacts());
    [TestMethod] public void ValidationOutput_DoesNotSatisfyApprovalRequirement() => AssertNoApproval(Facts("validation_output", "passed", satisfiesApprovalRequirement: false));
    [TestMethod] public void RunReport_DoesNotAllowRelease() => AssertNoApproval(SuccessfulRunReportFacts() with { ApprovesRelease = false });

    [TestMethod]
    public void RunReportSuccess_DoesNotSayApproved()
    {
        var report = SuccessfulRunReport();

        Assert.AreEqual("succeeded", report.Status);
        Assert.IsFalse(string.Equals("approved", report.Data.Governance.ApprovalDecision, StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(string.Equals("release_approved", report.Data.Governance.ApprovalDecision, StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void ModelSaysApproved_IsRejectedAsApprovalEvidence()
    {
        var result = SafeCriticReview() with
        {
            Warnings = ["Model says approved.", "Critic findings are recommendations only.", "This review does not grant or deny approval.", "Governance and human approval remain separate."]
        };

        AssertHasIssue(new CriticReviewResultValidator().Validate(result), CriticReviewResultValidator.AuthorityClaimBlocked);
    }

    [TestMethod] public void ModelSaysLooksGood_IsNotApproval() => AssertNoApproval(Facts("model_output", "looks_good"));
    [TestMethod] public void RetrieverMatch_IsNotApproval() => AssertNoApproval(Facts("retrieval_match", "matched"));
    [TestMethod] public void VectorMatch_IsNotApproval() => AssertNoApproval(Facts("vector_match", "matched"));
    [TestMethod] public void ModelOutput_DoesNotSatisfyApprovalRequirement() => AssertNoApproval(Facts("model_output", "looks_good", satisfiesApprovalRequirement: false));
    [TestMethod] public void A2aHandoffEvidence_IsNotApproval() => AssertNoApproval(Facts("a2a_handoff_evidence", "available"));
    [TestMethod] public void A2aHandoffEvidence_DoesNotTransferAuthority() => AssertNoApproval(Facts("a2a_handoff_evidence", "available", transfersAuthority: false));
    [TestMethod] public void WorkflowRoute_IsNotApproval() => AssertNoApproval(Facts("workflow_route", "selected"));
    [TestMethod] public void WorkflowRoute_DoesNotStartWorkflow() => AssertNoApproval(Facts("workflow_route", "selected", startsWorkflow: false));
    [TestMethod] public void ApprovalRecord_IsStillNotExecution() => AssertNoApproval(Facts("approval_decision_record", "approved", grantsApproval: true, canGrantApproval: true));
    [TestMethod] public void ApprovalRecord_DoesNotMutateSource() => AssertNoApproval(Facts("approval_decision_record", "approved", grantsApproval: true, mutatesSource: false, canGrantApproval: true));
    [TestMethod] public void ApprovalRecord_DoesNotPromoteMemory() => AssertNoApproval(Facts("approval_decision_record", "approved", grantsApproval: true, promotesMemory: false, canGrantApproval: true));
    [TestMethod] public void GateDecisionAuthorityFlags_AreAlwaysSafe() => AssertNoActiveEffects(AllowedGateDecision());
    [TestMethod] public void PolicyDecisionAuthorityFlags_AreAlwaysFalse() => AssertNoApproval(Facts("policy_decision_event", nameof(PolicyDecisionValue.NoPolicyBlock)));
    [TestMethod] public void DogfoodReceiptAuthorityFlags_AreAlwaysFalse() => AssertNoApproval(Facts("dogfood_receipt", "passed"));
    [TestMethod] public void ApprovalRequirementEvaluationAuthorityFlags_AreAlwaysFalse() => AssertNoApproval(ApprovalRequiredEvaluation());
    [TestMethod] public void ApprovalPackageAuthorityFlags_AreAlwaysFalse() => AssertNoApproval(ReadyForReviewPackage());
    [TestMethod] public void PolicyProfileAuthorityFlags_AreAlwaysFalse() => ProjectPolicyProfileCatalog.All.ToList().ForEach(profile => AssertNoApproval(ProfileFacts(profile.ProfileName)));
    [TestMethod] public void ThoughtLedgerReferenceAuthorityFlags_AreAlwaysFalse() => AssertNoApproval(ThoughtLedgerFacts());

    [TestMethod]
    [TestCategory("ApprovalAuthorityWording")]
    public void ApprovalBoundaryWording_DocumentationStatesRequiredBoundary()
    {
        var doc = BlockHDoc();

        StringAssert.Contains(doc, "PR88 Approval Is Not Gate/Receipt/Critic Test Pack");
        StringAssert.Contains(doc, "Approval is explicit.");
        StringAssert.Contains(doc, "Approval cannot be inferred from gate decisions, policy decisions, dogfood receipts, critic output, validation output, model output, retrieval output, approval packages, policy profiles, ThoughtLedger references, or governance events.");
        StringAssert.Contains(doc, "ReadyForReview is not Approved.");
        StringAssert.Contains(doc, "NoPolicyBlock is not Approved.");
        StringAssert.Contains(doc, "Dogfood Passed is not ReleaseApproved.");
        StringAssert.Contains(doc, "Experimental is not Free.");
        StringAssert.Contains(doc, "Gate Passed is not Approved.");
    }

    private static void AssertCriticIsNotApproval(CriticReviewVerdict verdict)
    {
        var result = SafeCriticReview(verdict);

        Assert.AreEqual(0, new CriticReviewResultValidator().Validate(result).Count);
        AssertNoApproval(Facts("critic_review", verdict.ToString()));
    }

    private static void AssertNoApproval(AgentToolExecutionGateDecision decision)
    {
        Assert.IsFalse(decision.ExecutesTool);
        Assert.IsFalse(decision.MutatesSource);
        Assert.IsFalse(decision.CallsExternalSystem);
        Assert.IsFalse(decision.SubmitsGitHubReview);
        Assert.IsFalse(decision.PersistsResult);
        Assert.IsFalse(decision.PromotesMemory);
        Assert.IsFalse(decision.CreatesCollectiveMemory);
        Assert.IsFalse(decision.WritesWeaviate);
    }

    private static void AssertNoApproval(ApprovalRequirementEvaluationResult result)
    {
        Assert.IsFalse(result.GrantsApproval);
        Assert.IsFalse(result.GrantsExecution);
        Assert.IsFalse(result.MutatesSource);
        Assert.IsFalse(result.PromotesMemory);
        Assert.IsFalse(result.StartsWorkflow);
        Assert.IsFalse(result.SatisfiesPolicy);
        Assert.IsFalse(result.TransfersAuthority);
    }

    private static void AssertNoApproval(ApprovalPackage package)
    {
        Assert.IsFalse(package.GrantsApproval);
        Assert.IsFalse(package.GrantsExecution);
        Assert.IsFalse(package.MutatesSource);
        Assert.IsFalse(package.PromotesMemory);
        Assert.IsFalse(package.StartsWorkflow);
        Assert.IsFalse(package.SatisfiesPolicy);
        Assert.IsFalse(package.TransfersAuthority);
    }

    private static void AssertNoApproval(BoundaryFacts facts)
    {
        if (!facts.CanGrantApproval)
            Assert.IsFalse(facts.GrantsApproval, facts.Name);

        Assert.IsFalse(facts.GrantsExecution, facts.Name);
        Assert.IsFalse(facts.MutatesSource, facts.Name);
        Assert.IsFalse(facts.PromotesMemory, facts.Name);
        Assert.IsFalse(facts.StartsWorkflow, facts.Name);
        Assert.IsFalse(facts.SatisfiesPolicy, facts.Name);
        Assert.IsFalse(facts.TransfersAuthority, facts.Name);
        Assert.IsFalse(facts.CreatesApprovalDecision, facts.Name);
        Assert.IsFalse(facts.SatisfiesApprovalRequirement, facts.Name);
        Assert.IsFalse(facts.ApprovesRelease, facts.Name);
    }

    private static void AssertNoActiveEffects(AgentToolExecutionGateDecision decision)
    {
        Assert.IsFalse(decision.ExecutesTool);
        Assert.IsFalse(decision.MutatesSource);
        Assert.IsFalse(decision.CallsExternalSystem);
        Assert.IsFalse(decision.SubmitsGitHubReview);
        Assert.IsFalse(decision.PersistsResult);
        Assert.IsFalse(decision.PromotesMemory);
        Assert.IsFalse(decision.CreatesCollectiveMemory);
        Assert.IsFalse(decision.WritesWeaviate);
    }

    private static void AssertProfileIsNotAuthority(string profileName)
    {
        var profile = ProjectPolicyProfileCatalog.Get(profileName);

        Assert.AreEqual(profileName, profile.ProfileName);
        Assert.AreEqual(0, ProjectPolicyProfileValidator.Validate(profile).Issues.Count);
        AssertNoApproval(ProfileFacts(profileName));
    }

    private static void AssertHasIssue<TIssue>(IReadOnlyList<TIssue> issues, string code)
    {
        var hasIssue = issues.Any(issue => string.Equals((string?)issue!.GetType().GetProperty("Code")?.GetValue(issue), code, StringComparison.Ordinal));
        Assert.IsTrue(hasIssue, $"Expected issue {code}. Actual: {string.Join(", ", issues.Select(issue => issue!.GetType().GetProperty("Code")?.GetValue(issue)))}");
    }

    private static AgentToolExecutionGateDecision AllowedGateDecision()
    {
        var result = new AgentToolExecutionGate().Evaluate(GateRequest(ValidReadOnlyRequest(), ReadOnlyPolicy()));
        Assert.IsTrue(result.Succeeded);
        Assert.IsNotNull(result.Decision);
        Assert.AreEqual(AgentToolExecutionGateDecisionType.Allowed, result.Decision.Decision);
        return result.Decision;
    }

    private static AgentToolExecutionGateDecision RequiresApprovalGateDecision()
    {
        var request = BuildToolRequest(AgentDefinitionCatalog.TestingAgent, AgentToolRequestType.BuildExecutionRequest, AgentToolKind.BuildRun, AgentToolRiskLevel.Medium, new AgentToolRequestApprovalRequirement { RequiresGovernanceGate = true });
        var result = new AgentToolExecutionGate().Evaluate(GateRequest(request, BuildPolicy()));
        Assert.IsTrue(result.Succeeded);
        Assert.IsNotNull(result.Decision);
        Assert.AreEqual(AgentToolExecutionGateDecisionType.RequiresApproval, result.Decision.Decision);
        return result.Decision;
    }

    private static AgentToolExecutionGateDecision BlockedGateDecision()
    {
        var result = new AgentToolExecutionGate().Evaluate(GateRequest(ValidReadOnlyRequest(), ReadOnlyPolicy() with { AllowsToolRequest = false }));
        Assert.IsTrue(result.Succeeded);
        Assert.IsNotNull(result.Decision);
        Assert.AreEqual(AgentToolExecutionGateDecisionType.Blocked, result.Decision.Decision);
        return result.Decision;
    }

    private static AgentToolRequest ValidReadOnlyRequest() =>
        BuildToolRequest(AgentDefinitionCatalog.ReportingAgent, AgentToolRequestType.ReadOnlyInspection, AgentToolKind.WorkspaceDiff, AgentToolRiskLevel.Low);

    private static AgentToolRequest BuildToolRequest(AgentDefinition agent, AgentToolRequestType requestType, AgentToolKind toolKind, AgentToolRiskLevel riskLevel, AgentToolRequestApprovalRequirement? approval = null) =>
        new()
        {
            ToolRequestId = "tool-request-1",
            Status = AgentToolRequestStatus.PendingGate,
            RequestType = requestType,
            ToolKind = toolKind,
            RiskLevel = riskLevel,
            Scope = new AgentToolRequestScope
            {
                TenantId = "tenant-1",
                ProjectId = "project-1",
                CampaignId = "campaign-1",
                RunId = "run-1",
                AgentRunId = "agent-run-1",
                CorrelationId = "correlation-1"
            },
            Actor = new AgentToolRequestActor
            {
                AgentId = agent.AgentId,
                AgentName = agent.Name,
                AgentKind = agent.Kind,
                ExecutionMode = agent.ExecutionMode,
                DeclaredCapabilities = agent.Capabilities?.ToArray() ?? [],
                ForbiddenCapabilities = agent.ForbiddenCapabilities?.ToArray() ?? []
            },
            Purpose = "Evaluate a request without granting approval.",
            Inputs =
            [
                new AgentToolRequestInput
                {
                    InputId = "input-1",
                    RefType = "WorkspaceReport",
                    RefId = "workspace-report-1",
                    Source = "test",
                    Summary = "Safe input summary.",
                    EvidenceRefs = ["evidence-1"],
                    IsSanitised = true
                }
            ],
            Evidence =
            [
                new AgentToolRequestEvidence
                {
                    EvidenceId = "evidence-1",
                    RefType = "SourceReport",
                    RefId = "source-report-1",
                    Summary = "Evidence for review only.",
                    SupportsNeedForTool = true
                }
            ],
            ApprovalRequirement = approval ?? new AgentToolRequestApprovalRequirement(),
            PolicySnapshot = new AgentToolRequestPolicySnapshot
            {
                PolicyKnown = true,
                AllowsToolRequest = true,
                PolicyRefs = ["policy:test"]
            },
            RequestedAtUtc = FixedUtc
        };

    private static AgentToolExecutionGateRequest GateRequest(AgentToolRequest request, AgentToolExecutionGatePolicyContext policy) =>
        new()
        {
            ToolRequest = request,
            PolicyContext = policy,
            EvaluatedAtUtc = FixedUtc
        };

    private static AgentToolExecutionGatePolicyContext ReadOnlyPolicy() =>
        new()
        {
            PolicyKnown = true,
            AllowsToolRequest = true,
            PolicyRefs = ["policy:read"]
        };

    private static AgentToolExecutionGatePolicyContext BuildPolicy() =>
        ReadOnlyPolicy() with
        {
            AllowsToolExecution = true,
            AllowsBuildExecution = true
        };

    private static ApprovalRequirementEvaluationResult ApprovalRequiredEvaluation() =>
        new ApprovalRequirementEvaluator().Evaluate(ValidEvaluationRequest(ActiveRule(ProjectApprovalRuleApprovalTypes.HumanOnly, [ProjectApprovalRuleApproverTypes.ProjectLead])));

    private static ApprovalRequirementEvaluationResult NoApprovalRequiredEvaluation() =>
        new ApprovalRequirementEvaluator().Evaluate(ValidEvaluationRequest(ActiveRule(ProjectApprovalRuleApprovalTypes.None, [])));

    private static ApprovalRequirementEvaluationRequest ValidEvaluationRequest(ProjectApprovalRule rule) =>
        new()
        {
            ProjectId = ProjectId,
            ProjectPolicy = ActivePolicy(),
            ApprovalRules = [rule],
            ApprovalScope = ProjectApprovalRuleScopes.ToolExecution,
            SubjectType = "tool_request",
            SubjectId = "tool-request-1",
            ActionName = "workspace.diff",
            RequestedByActorType = "human",
            RequestedByActorId = "human-reviewer",
            ContextVersion = 1,
            ContextJson = "{\"schema\":\"approval.requirement.context.v1\"}"
        };

    private static ProjectAutonomyPolicy ActivePolicy() =>
        new()
        {
            ProjectAutonomyPolicyId = PolicyId,
            ProjectId = ProjectId,
            PolicyName = "Project authority policy",
            PolicyVersion = 1,
            AutonomyLevel = nameof(ProjectAutonomyLevel.Balanced),
            Status = nameof(ProjectAutonomyPolicyStatus.Active),
            CreatedByActorType = "human",
            CreatedByActorId = "human-reviewer",
            MetadataVersion = 1,
            MetadataJson = "{\"schema\":\"project.autonomy.policy.metadata.v1\"}",
            CreatedUtc = FixedUtc
        };

    private static ProjectApprovalRule ActiveRule(string approvalType, IReadOnlyList<string> approvers) =>
        new()
        {
            ProjectApprovalRuleId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            ProjectId = ProjectId,
            ProjectAutonomyPolicyId = PolicyId,
            RuleName = "Project approval rule",
            RuleVersion = 1,
            Status = ProjectApprovalRuleStatuses.Active,
            ApprovalScope = ProjectApprovalRuleScopes.ToolExecution,
            SubjectTypePattern = "tool_request",
            ActionNamePattern = "workspace.diff",
            RiskLevel = ProjectApprovalRuleRiskLevels.Medium,
            ApprovalType = approvalType,
            ApproverTypes = approvers,
            CreatedByActorType = "human",
            CreatedByActorId = "human-reviewer",
            MetadataVersion = 1,
            MetadataJson = "{\"schema\":\"project.approval.rule.metadata.v1\"}",
            CreatedUtc = FixedUtc
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
                new ApprovalPackageEvidenceReference { EvidenceType = ApprovalPackageEvidenceTypes.ToolGateDecision, EvidenceId = "gate-decision-1" },
                new ApprovalPackageEvidenceReference { EvidenceType = ApprovalPackageEvidenceTypes.DogfoodReceipt, EvidenceId = "dogfood-receipt-1" }
            ],
            CreatedByActorType = "human",
            CreatedByActorId = "human-reviewer",
            MetadataVersion = 1,
            MetadataJson = "{\"schema\":\"approval.package.metadata.v1\"}",
            GrantsApproval = false,
            GrantsExecution = false,
            MutatesSource = false,
            PromotesMemory = false,
            StartsWorkflow = false,
            SatisfiesPolicy = false,
            TransfersAuthority = false,
            CreatedUtc = FixedUtc
        };

    private static PolicyDecisionRecordRequest PolicyDecisionRecord() =>
        new()
        {
            ProjectId = ProjectId,
            PolicyScope = PolicyDecisionScopes.ToolExecution,
            PolicyName = "tool-execution-policy",
            PolicyVersion = 1,
            SubjectType = "tool_request",
            SubjectId = "tool-request-1",
            Decision = nameof(PolicyDecisionValue.NoPolicyBlock),
            RequirementCode = "NO_POLICY_BLOCK",
            ReasonCode = "POLICY_CHECK_RECORDED",
            Reason = "Policy check evidence only.",
            DecidedByActorType = "system",
            DecidedByActorId = "policy-test",
            EvidenceVersion = 1,
            EvidenceJson = "{\"schema\":\"policy.decision.evidence.v1\",\"grantsApproval\":false,\"grantsExecution\":false,\"satisfiesPolicy\":false}"
        };

    private static DogfoodReceiptRecordRequest DogfoodReceiptRecord() =>
        new()
        {
            ProjectId = ProjectId,
            ReceiptType = "dogfood_loop",
            SubjectType = "dogfood_loop",
            SubjectId = "dogfood-loop-1",
            Outcome = nameof(DogfoodReceiptOutcome.Passed),
            SummaryCode = "DOGFOOD_LOOP_RECORDED",
            Summary = "Dogfood receipt evidence for human review.",
            RecordedByActorType = "system_test_fixture",
            RecordedByActorId = "dogfood-test",
            EvidenceVersion = 1,
            EvidenceJson = "{\"schema\":\"dogfood.receipt.evidence.v1\",\"grantsApproval\":false,\"approvesRelease\":false,\"grantsExecution\":false,\"satisfiesPolicy\":false}"
        };

    private static CriticReviewResult SafeCriticReview(CriticReviewVerdict verdict = CriticReviewVerdict.NoObjection) =>
        new()
        {
            ReviewResultId = "critic-review-result-1",
            ReviewRequestId = "critic-review-request-1",
            Verdict = verdict,
            Findings = verdict == CriticReviewVerdict.NoObjection ? [] : [SafeFinding()],
            ReviewedAt = FixedUtc,
            ReviewedByAgentId = "builtin.independent-critic",
            CorrelationId = "correlation-1",
            Warnings =
            [
                "Critic findings are recommendations only.",
                "This review does not grant or deny approval.",
                "Governance and human approval remain separate."
            ]
        };

    private static CriticFinding SafeFinding() =>
        new()
        {
            FindingId = "finding-1",
            Severity = CriticSeverity.Medium,
            Title = "Review finding",
            Problem = "There is something to inspect.",
            WhyItMatters = "The human reviewer needs evidence.",
            RequiredFix = "Review the evidence manually.",
            EvidenceRefs = ["evidence-1"],
            BlocksMerge = true,
            RequiresHumanReview = true
        };

    private static IronDev.Core.Agents.Audit.ThoughtLedgerEntry SafeThoughtLedgerEntry() =>
        new()
        {
            ThoughtLedgerEntryId = "thought-1",
            AgentRunId = "agent-run-1",
            EntryType = ThoughtLedgerEntryType.EvidenceUsed,
            Summary = "Referenced governance evidence for review.",
            EvidenceRefs = ["governance-event-1"],
            ContainsRawPrivateReasoning = false,
            GrantsAuthority = false,
            GrantsApproval = false,
            GrantsMemoryPromotion = false,
            RecordedAtUtc = FixedUtc
        };

    private static RunReportContractReadResult SuccessfulRunReport() =>
        new()
        {
            Status = "succeeded",
            Command = "runs report",
            Summary = "Run report succeeded.",
            Data = new RunReportContractData
            {
                RunId = "run-1",
                RunStatus = "Completed",
                Governance = new RunReportGovernanceContractData
                {
                    Decision = "derived",
                    ApprovalDecision = "not_required",
                    RequiresHumanApproval = false
                }
            },
            ExitCode = 0
        };

    private static BoundaryFacts ThoughtLedgerFacts() => Facts("thoughtledger_governance_reference", "referenced");
    private static BoundaryFacts SuccessfulRunReportFacts() => Facts("run_report", "succeeded");

    private static BoundaryFacts ProfileFacts(string profileName)
    {
        var profile = ProjectPolicyProfileCatalog.Get(profileName);
        return new BoundaryFacts(
            Name: $"policy_profile:{profile.ProfileName}",
            GrantsApproval: profile.GrantsApproval,
            GrantsExecution: profile.GrantsExecution,
            MutatesSource: profile.MutatesSource,
            PromotesMemory: profile.PromotesMemory,
            StartsWorkflow: profile.StartsWorkflow,
            SatisfiesPolicy: profile.SatisfiesPolicy,
            TransfersAuthority: profile.TransfersAuthority,
            CreatesApprovalDecision: false,
            SatisfiesApprovalRequirement: false,
            ApprovesRelease: false,
            CanGrantApproval: false);
    }

    private static BoundaryFacts Facts(
        string name,
        string state,
        bool grantsApproval = false,
        bool grantsExecution = false,
        bool mutatesSource = false,
        bool promotesMemory = false,
        bool startsWorkflow = false,
        bool satisfiesPolicy = false,
        bool transfersAuthority = false,
        bool createsApprovalDecision = false,
        bool satisfiesApprovalRequirement = false,
        bool approvesRelease = false,
        bool canGrantApproval = false) =>
        new(
            $"{name}:{state}",
            grantsApproval,
            grantsExecution,
            mutatesSource,
            promotesMemory,
            startsWorkflow,
            satisfiesPolicy,
            transfersAuthority,
            createsApprovalDecision,
            satisfiesApprovalRequirement,
            approvesRelease,
            canGrantApproval);

    private static string BlockHDoc() => File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "BLOCK_H_PROJECT_AUTHORITY_POLICY_MODEL.md"));

    private static void AssertFileDoesNotContain(string relativePath, params string[] tokens)
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), relativePath));
        foreach (var token in tokens)
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), token);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }

    private sealed record BoundaryFacts(
        string Name,
        bool GrantsApproval,
        bool GrantsExecution,
        bool MutatesSource,
        bool PromotesMemory,
        bool StartsWorkflow,
        bool SatisfiesPolicy,
        bool TransfersAuthority,
        bool CreatesApprovalDecision,
        bool SatisfiesApprovalRequirement,
        bool ApprovesRelease,
        bool CanGrantApproval);
}
