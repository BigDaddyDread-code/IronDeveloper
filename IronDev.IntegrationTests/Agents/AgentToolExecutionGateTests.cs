using IronDev.Core.Agents;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Agents;

[TestClass]
public sealed class AgentToolExecutionGateTests
{
    private static readonly DateTimeOffset RequestedAt = new(2026, 6, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset EvaluatedAt = new(2026, 6, 11, 12, 30, 0, TimeSpan.Zero);

    [TestMethod]
    public void AgentToolExecutionGateContracts_ExposeExpectedCoreTypes()
    {
        Assert.IsNotNull(typeof(IAgentToolExecutionGate));
        Assert.IsNotNull(typeof(AgentToolExecutionGate));
        Assert.IsNotNull(typeof(AgentToolExecutionGateRequest));
        Assert.IsNotNull(typeof(AgentToolExecutionGateResult));
        Assert.IsNotNull(typeof(AgentToolExecutionGateDecision));
        Assert.IsNotNull(typeof(AgentToolExecutionGateDecisionType));
        Assert.IsNotNull(typeof(AgentToolExecutionGateReason));
        Assert.IsNotNull(typeof(AgentToolExecutionGateIssue));
        Assert.IsNotNull(typeof(AgentToolExecutionGatePolicyContext));
        Assert.IsNotNull(typeof(AgentToolExecutionGateApprovalContext));
        Assert.IsNotNull(typeof(AgentToolExecutionGateMemoryContext));
    }

    [TestMethod]
    public void AgentToolExecutionGateDecisionType_DoesNotExposeExecutionLifecycleStates()
    {
        var names = Enum.GetNames<AgentToolExecutionGateDecisionType>();

        CollectionAssert.AreEquivalent(
            new[] { "Blocked", "RequiresApproval", "Allowed" },
            names);

        var forbidden = new[] { "Executing", "Executed", "Succeeded", "Failed", "Completed" };
        foreach (var name in forbidden)
            Assert.IsFalse(names.Contains(name, StringComparer.Ordinal), $"Forbidden gate decision state was exposed: {name}");
    }

    [TestMethod]
    public void AgentToolExecutionGate_ValidAnalyseOnlyRequest_AllowsForFutureExecutorOnly()
    {
        var request = BuildRequest(
            AgentDefinitionCatalog.IndependentCriticAgent,
            AgentToolRequestType.AnalyseOnly,
            AgentToolKind.CodeStandardsAnalysePatch,
            AgentToolRiskLevel.Low);

        var result = DefaultGate().Evaluate(GateRequest(request, ReadOnlyPolicy()));

        var decision = AssertDecision(result, AgentToolExecutionGateDecisionType.Allowed);
        AssertHasReason(decision, AgentToolExecutionGate.ToolGateAllowedForFutureExecutor);
        AssertFutureExecutorOnly(decision);
    }

    [TestMethod]
    public void AgentToolExecutionGate_ValidReadOnlyInspectionRequest_AllowsForFutureExecutorOnly()
    {
        var request = BuildRequest(
            AgentDefinitionCatalog.ReportingAgent,
            AgentToolRequestType.ReadOnlyInspection,
            AgentToolKind.WorkspaceDiff,
            AgentToolRiskLevel.Low);

        var result = DefaultGate().Evaluate(GateRequest(request, ReadOnlyPolicy()));

        var decision = AssertDecision(result, AgentToolExecutionGateDecisionType.Allowed);
        AssertFutureExecutorOnly(decision);
    }

    [TestMethod]
    public void AgentToolExecutionGate_ValidPatchProposalRequest_AllowsWithoutSourceMutationPolicy()
    {
        var request = BuildRequest(
            AgentDefinitionCatalog.ImplementationAgent,
            AgentToolRequestType.PatchProposalRequest,
            AgentToolKind.PatchProposal,
            AgentToolRiskLevel.Medium);

        var result = DefaultGate().Evaluate(GateRequest(request, PatchProposalPolicy()));

        var decision = AssertDecision(result, AgentToolExecutionGateDecisionType.Allowed);
        AssertFutureExecutorOnly(decision);
    }

    [TestMethod]
    public void AgentToolExecutionGate_BuildAndTestRequests_AllowOnlyWithGovernanceGateApproval()
    {
        var build = BuildRequest(
            AgentDefinitionCatalog.TestingAgent,
            AgentToolRequestType.BuildExecutionRequest,
            AgentToolKind.BuildRun,
            AgentToolRiskLevel.Medium,
            approval: GateOnlyApproval());
        var test = BuildRequest(
            AgentDefinitionCatalog.TestingAgent,
            AgentToolRequestType.TestExecutionRequest,
            AgentToolKind.TestRun,
            AgentToolRiskLevel.Medium,
            approval: GateOnlyApproval());

        var buildDecision = AssertDecision(
            DefaultGate().Evaluate(GateRequest(build, BuildPolicy(), GovernanceApproval())),
            AgentToolExecutionGateDecisionType.Allowed);
        var testDecision = AssertDecision(
            DefaultGate().Evaluate(GateRequest(test, TestPolicy(), GovernanceApproval())),
            AgentToolExecutionGateDecisionType.Allowed);

        AssertFutureExecutorOnly(buildDecision);
        AssertFutureExecutorOnly(testDecision);
    }

    [TestMethod]
    public void AgentToolExecutionGate_SourceMutationRequest_AllowsOnlyWithHumanPolicyGovernanceAndDryRun()
    {
        var request = BuildRequest(
            AgentDefinitionCatalog.ImplementationAgent,
            AgentToolRequestType.SourceMutationRequest,
            AgentToolKind.SourceApply,
            AgentToolRiskLevel.Critical,
            approval: SourceMutationApproval());

        var result = DefaultGate().Evaluate(GateRequest(request, SourceMutationPolicy(), FullSourceApproval()));

        var decision = AssertDecision(result, AgentToolExecutionGateDecisionType.Allowed);
        AssertFutureExecutorOnly(decision);
    }

    [TestMethod]
    public void AgentToolExecutionGate_ExternalRequests_AllowOnlyWithRequiredApprovalsAndPolicy()
    {
        var externalAgent = BuildExternalAgentDefinition();
        var gate = ExternalGate(externalAgent);
        var externalHttp = BuildRequest(
            externalAgent,
            AgentToolRequestType.ExternalEffectRequest,
            AgentToolKind.ExternalHttpCall,
            AgentToolRiskLevel.Critical,
            approval: ExternalEffectApproval());
        var github = BuildRequest(
            externalAgent,
            AgentToolRequestType.ExternalEffectRequest,
            AgentToolKind.GitHubReviewSubmission,
            AgentToolRiskLevel.Critical,
            approval: ExternalEffectApproval());

        var externalDecision = AssertDecision(
            gate.Evaluate(GateRequest(externalHttp, ExternalEffectPolicy(), FullExternalApproval())),
            AgentToolExecutionGateDecisionType.Allowed);
        var githubDecision = AssertDecision(
            gate.Evaluate(GateRequest(github, GitHubSubmissionPolicy(), FullExternalApproval())),
            AgentToolExecutionGateDecisionType.Allowed);

        AssertFutureExecutorOnly(externalDecision);
        AssertFutureExecutorOnly(githubDecision);
    }

    [TestMethod]
    public void AgentToolExecutionGate_MemoryBackedRequest_AllowsOnlyWithMemoryGovernanceAllow()
    {
        var request = BuildRequest(
            AgentDefinitionCatalog.ReportingAgent,
            AgentToolRequestType.ReadOnlyInspection,
            AgentToolKind.WorkspaceDiff,
            AgentToolRiskLevel.Low,
            input: ValidInput() with { RefType = "AgentLocalMemoryItem" },
            approval: MemoryGovernanceApproval());

        var result = DefaultGate().Evaluate(GateRequest(request, ReadOnlyPolicy(), memory: MemoryAllowed()));

        var decision = AssertDecision(result, AgentToolExecutionGateDecisionType.Allowed);
        AssertFutureExecutorOnly(decision);
    }

    [TestMethod]
    public void AgentToolExecutionGate_InvalidAgentToolRequest_Blocks()
    {
        var request = ValidReadOnlyRequest() with { ToolRequestId = string.Empty };

        var result = DefaultGate().Evaluate(GateRequest(request, ReadOnlyPolicy()));

        var decision = AssertDecision(result, AgentToolExecutionGateDecisionType.Blocked);
        AssertHasIssue(decision, AgentToolExecutionGate.ToolGateRequestInvalid);
        AssertNoFutureExecutor(decision);
    }

    [TestMethod]
    public void AgentToolExecutionGate_NonExecutableRequestClaims_Block()
    {
        var cases = new[]
        {
            ValidReadOnlyRequest() with { ClaimsApproval = true },
            ValidReadOnlyRequest() with { ClaimsExecutionPermission = true },
            ValidReadOnlyRequest() with { ContainsExecutionResult = true },
            ValidReadOnlyRequest() with { IsExecutableWithoutGate = true }
        };

        foreach (var request in cases)
        {
            var decision = AssertDecision(
                DefaultGate().Evaluate(GateRequest(request, ReadOnlyPolicy())),
                AgentToolExecutionGateDecisionType.Blocked);
            AssertHasIssue(decision, AgentToolExecutionGate.ToolGateBlockedNonExecutableRequest);
            AssertNoFutureExecutor(decision);
        }
    }

    [TestMethod]
    public void AgentToolExecutionGate_PolicyBlockingToolRequest_Blocks()
    {
        var request = ValidReadOnlyRequest();

        var result = DefaultGate().Evaluate(GateRequest(request, ReadOnlyPolicy() with { AllowsToolRequest = false }));

        var decision = AssertDecision(result, AgentToolExecutionGateDecisionType.Blocked);
        AssertHasIssue(decision, AgentToolExecutionGate.ToolGateToolRequestBlockedByPolicy);
        AssertNoFutureExecutor(decision);
    }

    [TestMethod]
    public void AgentToolExecutionGate_PolicyBlockingToolExecution_BlocksExecutionLikeRequests()
    {
        var build = BuildRequest(
            AgentDefinitionCatalog.TestingAgent,
            AgentToolRequestType.BuildExecutionRequest,
            AgentToolKind.BuildRun,
            AgentToolRiskLevel.Medium,
            approval: GateOnlyApproval());

        var result = DefaultGate().Evaluate(GateRequest(build, BuildPolicy() with { AllowsToolExecution = false }, GovernanceApproval()));

        var decision = AssertDecision(result, AgentToolExecutionGateDecisionType.Blocked);
        AssertHasIssue(decision, AgentToolExecutionGate.ToolGateToolExecutionBlockedByPolicy);
        AssertNoFutureExecutor(decision);
    }

    [TestMethod]
    public void AgentToolExecutionGate_SpecificPolicyBlocks_AreEnforced()
    {
        var externalAgent = BuildExternalAgentDefinition();
        var externalGate = ExternalGate(externalAgent);
        var cases = new (IAgentToolExecutionGate Gate, AgentToolRequest Request, AgentToolExecutionGatePolicyContext Policy, AgentToolExecutionGateApprovalContext Approval, string Code)[]
        {
            (DefaultGate(), BuildRequest(AgentDefinitionCatalog.TestingAgent, AgentToolRequestType.BuildExecutionRequest, AgentToolKind.BuildRun, AgentToolRiskLevel.Medium, approval: GateOnlyApproval()), BuildPolicy() with { AllowsBuildExecution = false }, GovernanceApproval(), AgentToolExecutionGate.ToolGateBuildBlockedByPolicy),
            (DefaultGate(), BuildRequest(AgentDefinitionCatalog.TestingAgent, AgentToolRequestType.TestExecutionRequest, AgentToolKind.TestRun, AgentToolRiskLevel.Medium, approval: GateOnlyApproval()), TestPolicy() with { AllowsTestExecution = false }, GovernanceApproval(), AgentToolExecutionGate.ToolGateTestBlockedByPolicy),
            (DefaultGate(), BuildRequest(AgentDefinitionCatalog.ImplementationAgent, AgentToolRequestType.PatchProposalRequest, AgentToolKind.PatchProposal, AgentToolRiskLevel.Medium), PatchProposalPolicy() with { AllowsPatchProposal = false }, new(), AgentToolExecutionGate.ToolGatePatchProposalBlockedByPolicy),
            (DefaultGate(), BuildRequest(AgentDefinitionCatalog.ImplementationAgent, AgentToolRequestType.SourceMutationRequest, AgentToolKind.SourceApply, AgentToolRiskLevel.Critical, approval: SourceMutationApproval()), SourceMutationPolicy() with { AllowsSourceMutation = false }, FullSourceApproval(), AgentToolExecutionGate.ToolGateSourceMutationBlockedByPolicy),
            (externalGate, BuildRequest(externalAgent, AgentToolRequestType.ExternalEffectRequest, AgentToolKind.ExternalHttpCall, AgentToolRiskLevel.Critical, approval: ExternalEffectApproval()), ExternalEffectPolicy() with { AllowsExternalEffects = false }, FullExternalApproval(), AgentToolExecutionGate.ToolGateExternalEffectBlockedByPolicy),
            (externalGate, BuildRequest(externalAgent, AgentToolRequestType.ExternalEffectRequest, AgentToolKind.GitHubReviewSubmission, AgentToolRiskLevel.Critical, approval: ExternalEffectApproval()), GitHubSubmissionPolicy() with { AllowsGitHubSubmission = false }, FullExternalApproval(), AgentToolExecutionGate.ToolGateGitHubSubmissionBlockedByPolicy)
        };

        foreach (var testCase in cases)
        {
            var decision = AssertDecision(
                testCase.Gate.Evaluate(GateRequest(testCase.Request, testCase.Policy, testCase.Approval)),
                AgentToolExecutionGateDecisionType.Blocked);
            AssertHasIssue(decision, testCase.Code);
            AssertNoFutureExecutor(decision);
        }
    }

    [TestMethod]
    public void AgentToolExecutionGate_ApprovalMissing_ReturnsRequiresApproval()
    {
        var build = BuildRequest(
            AgentDefinitionCatalog.TestingAgent,
            AgentToolRequestType.BuildExecutionRequest,
            AgentToolKind.BuildRun,
            AgentToolRiskLevel.Medium,
            approval: GateOnlyApproval());
        var source = BuildRequest(
            AgentDefinitionCatalog.ImplementationAgent,
            AgentToolRequestType.SourceMutationRequest,
            AgentToolKind.SourceApply,
            AgentToolRiskLevel.Critical,
            approval: SourceMutationApproval());
        var externalAgent = BuildExternalAgentDefinition();
        var external = BuildRequest(
            externalAgent,
            AgentToolRequestType.ExternalEffectRequest,
            AgentToolKind.ExternalHttpCall,
            AgentToolRiskLevel.Critical,
            approval: ExternalEffectApproval());

        var buildDecision = AssertDecision(DefaultGate().Evaluate(GateRequest(build, BuildPolicy())), AgentToolExecutionGateDecisionType.RequiresApproval);
        var sourceDecision = AssertDecision(DefaultGate().Evaluate(GateRequest(source, SourceMutationPolicy())), AgentToolExecutionGateDecisionType.RequiresApproval);
        var externalDecision = AssertDecision(ExternalGate(externalAgent).Evaluate(GateRequest(external, ExternalEffectPolicy())), AgentToolExecutionGateDecisionType.RequiresApproval);

        AssertHasIssue(buildDecision, AgentToolExecutionGate.ToolGateGovernanceApprovalRequired);
        AssertHasIssue(sourceDecision, AgentToolExecutionGate.ToolGateHumanApprovalRequired);
        AssertHasIssue(sourceDecision, AgentToolExecutionGate.ToolGatePolicyApprovalRequired);
        AssertHasIssue(sourceDecision, AgentToolExecutionGate.ToolGateGovernanceApprovalRequired);
        AssertHasIssue(sourceDecision, AgentToolExecutionGate.ToolGateDryRunRequired);
        AssertHasIssue(externalDecision, AgentToolExecutionGate.ToolGateHumanApprovalRequired);
        AssertHasIssue(externalDecision, AgentToolExecutionGate.ToolGatePolicyApprovalRequired);
        AssertHasIssue(externalDecision, AgentToolExecutionGate.ToolGateGovernanceApprovalRequired);
    }

    [TestMethod]
    public void AgentToolExecutionGate_UnknownPolicyRequiresApprovalForLowOrMediumAndBlocksHighOrCritical()
    {
        var low = ValidReadOnlyRequest();
        var critical = BuildRequest(
            AgentDefinitionCatalog.ImplementationAgent,
            AgentToolRequestType.SourceMutationRequest,
            AgentToolKind.SourceApply,
            AgentToolRiskLevel.Critical,
            approval: SourceMutationApproval());

        var lowDecision = AssertDecision(DefaultGate().Evaluate(GateRequest(low, new AgentToolExecutionGatePolicyContext())), AgentToolExecutionGateDecisionType.RequiresApproval);
        var criticalDecision = AssertDecision(DefaultGate().Evaluate(GateRequest(critical, new AgentToolExecutionGatePolicyContext(), FullSourceApproval())), AgentToolExecutionGateDecisionType.Blocked);

        AssertHasIssue(lowDecision, AgentToolExecutionGate.ToolGatePolicyUnknown);
        AssertHasIssue(criticalDecision, AgentToolExecutionGate.ToolGatePolicyUnknown);
    }

    [TestMethod]
    public void AgentToolExecutionGate_MemoryGovernanceOutcomes_AreFailClosed()
    {
        var request = BuildRequest(
            AgentDefinitionCatalog.ReportingAgent,
            AgentToolRequestType.ReadOnlyInspection,
            AgentToolKind.WorkspaceDiff,
            AgentToolRiskLevel.Low,
            input: ValidInput() with { RefType = "MemoryInfluenceRecord" },
            approval: MemoryGovernanceApproval());

        var missing = AssertDecision(DefaultGate().Evaluate(GateRequest(request, ReadOnlyPolicy())), AgentToolExecutionGateDecisionType.RequiresApproval);
        var blocked = AssertDecision(DefaultGate().Evaluate(GateRequest(request, ReadOnlyPolicy(), memory: MemoryBlocked())), AgentToolExecutionGateDecisionType.Blocked);
        var warnOnly = AssertDecision(DefaultGate().Evaluate(GateRequest(request, ReadOnlyPolicy(), memory: MemoryWarnOnly())), AgentToolExecutionGateDecisionType.RequiresApproval);
        var warnAndAllow = AssertDecision(DefaultGate().Evaluate(GateRequest(request, ReadOnlyPolicy(), memory: MemoryWarnAndAllow())), AgentToolExecutionGateDecisionType.Allowed);

        AssertHasIssue(missing, AgentToolExecutionGate.ToolGateMemoryGovernanceRequired);
        AssertHasIssue(blocked, AgentToolExecutionGate.ToolGateMemoryGovernanceBlocked);
        AssertHasIssue(warnOnly, AgentToolExecutionGate.ToolGateMemoryWarningNotAuthority);
        AssertHasIssue(warnAndAllow, AgentToolExecutionGate.ToolGateMemoryWarningNotAuthority);
        AssertFutureExecutorOnly(warnAndAllow);
    }

    [TestMethod]
    public void AgentToolExecutionGate_MemoryAllowDoesNotReplaceOtherApprovals()
    {
        var request = BuildRequest(
            AgentDefinitionCatalog.TestingAgent,
            AgentToolRequestType.BuildExecutionRequest,
            AgentToolKind.BuildRun,
            AgentToolRiskLevel.Medium,
            input: ValidInput() with { RefType = "AgentLocalMemoryItem" },
            approval: GateOnlyApproval() with { RequiresMemoryGovernance = true });

        var decision = AssertDecision(
            DefaultGate().Evaluate(GateRequest(request, BuildPolicy(), memory: MemoryAllowed())),
            AgentToolExecutionGateDecisionType.RequiresApproval);

        AssertHasIssue(decision, AgentToolExecutionGate.ToolGateGovernanceApprovalRequired);
        AssertNoFutureExecutor(decision);
    }

    [TestMethod]
    public void AgentToolExecutionGate_StaticBoundary_HasNoExecutionPersistenceRuntimeOrExternalTokens()
    {
        var source = ReadRepositoryFile("IronDev.Core", "Agents", "AgentToolExecutionGateModels.cs");
        var forbidden = new[]
        {
            "ProcessStartInfo",
            "System.Diagnostics.Process",
            "File.WriteAllText",
            "File.Delete",
            "File.Copy",
            "Directory.Delete",
            "SqlConnection",
            "DbConnection",
            "INSERT INTO",
            "UPDATE ",
            "DELETE ",
            "MERGE ",
            "HttpClient",
            "OpenAiLlmService",
            "ChatCompletion",
            "ResponsesApi",
            "WeaviateClient",
            "AddHostedService",
            "IHostedService",
            "BackgroundService",
            "AgentRuntime",
            "AgentScheduler",
            "AgentOrchestrator",
            "AgentToolRouter",
            "SubmitReview",
            "CreatePullRequest",
            "IAgentRunAuditEnvelopeStore",
            "SqlMemoryImprovementProposalStore",
            "SqlCollectiveMemoryPromotionService"
        };

        foreach (var token in forbidden)
            Assert.IsFalse(source.Contains(token, StringComparison.Ordinal), $"Forbidden active token found in AgentToolExecutionGateModels.cs: {token}");
    }

    [TestMethod]
    public void AgentToolExecutionGate_IsNotWiredIntoManualOrStoredExecutionPaths()
    {
        var files = new[]
        {
            ReadRepositoryFile("IronDev.Core", "Agents", "Concrete", "ManualIndependentCriticAgentService.cs"),
            ReadRepositoryFile("IronDev.Core", "Agents", "Concrete", "ManualMemoryImprovementAgentService.cs"),
            ReadRepositoryFile("IronDev.Core", "Agents", "Concrete", "ModelBackedManualIndependentCriticAgentService.cs"),
            ReadRepositoryFile("IronDev.Core", "Agents", "Concrete", "ModelBackedManualMemoryImprovementAgentService.cs"),
            ReadRepositoryFile("IronDev.Core", "Agents", "Concrete", "StoredManualAgentExecutionService.cs")
        };
        var forbidden = new[]
        {
            "IAgentToolExecutionGate",
            "AgentToolExecutionGate",
            "AgentToolExecutor",
            "IAgentToolExecutor",
            "AgentToolRouter",
            "IAgentToolRouter"
        };

        foreach (var file in files)
        {
            foreach (var token in forbidden)
                Assert.IsFalse(file.Contains(token, StringComparison.Ordinal), $"Runtime wiring token found: {token}");
        }
    }

    private static IAgentToolExecutionGate DefaultGate() => new AgentToolExecutionGate();

    private static IAgentToolExecutionGate ExternalGate(AgentDefinition externalAgent) =>
        new AgentToolExecutionGate(new AgentToolRequestValidator(AgentDefinitionCatalog.All.Concat([externalAgent]).ToArray()));

    private static AgentToolRequest ValidReadOnlyRequest() =>
        BuildRequest(
            AgentDefinitionCatalog.ReportingAgent,
            AgentToolRequestType.ReadOnlyInspection,
            AgentToolKind.WorkspaceDiff,
            AgentToolRiskLevel.Low);

    private static AgentToolRequest BuildRequest(
        AgentDefinition agent,
        AgentToolRequestType requestType,
        AgentToolKind toolKind,
        AgentToolRiskLevel riskLevel,
        AgentToolRequestInput? input = null,
        AgentToolRequestEvidence? evidence = null,
        AgentToolRequestApprovalRequirement? approval = null) =>
        new()
        {
            ToolRequestId = "tool-request-1",
            Status = AgentToolRequestStatus.PendingGate,
            RequestType = requestType,
            ToolKind = toolKind,
            RiskLevel = riskLevel,
            Scope = ValidScope(),
            Actor = ValidActor(agent),
            Purpose = "Evaluate whether this typed request can reach a future executor.",
            Inputs = [input ?? ValidInput()],
            Evidence = [evidence ?? ValidEvidence()],
            ApprovalRequirement = approval ?? new AgentToolRequestApprovalRequirement(),
            PolicySnapshot = new AgentToolRequestPolicySnapshot
            {
                PolicyKnown = true,
                AllowsToolRequest = true,
                PolicyRefs = ["policy:test"]
            },
            RequestedAtUtc = RequestedAt
        };

    private static AgentToolExecutionGateRequest GateRequest(
        AgentToolRequest request,
        AgentToolExecutionGatePolicyContext policy,
        AgentToolExecutionGateApprovalContext? approval = null,
        AgentToolExecutionGateMemoryContext? memory = null) =>
        new()
        {
            ToolRequest = request,
            PolicyContext = policy,
            ApprovalContext = approval ?? new AgentToolExecutionGateApprovalContext(),
            MemoryContext = memory ?? new AgentToolExecutionGateMemoryContext(),
            EvaluatedAtUtc = EvaluatedAt
        };

    private static AgentToolRequestScope ValidScope() =>
        new()
        {
            TenantId = "tenant-1",
            ProjectId = "project-1",
            CampaignId = "campaign-1",
            RunId = "run-1",
            AgentRunId = "agent-run-1",
            CorrelationId = "correlation-1"
        };

    private static AgentToolRequestActor ValidActor(AgentDefinition agent) =>
        new()
        {
            AgentId = agent.AgentId,
            AgentName = agent.Name,
            AgentKind = agent.Kind,
            ExecutionMode = agent.ExecutionMode,
            DeclaredCapabilities = agent.Capabilities?.ToArray() ?? [],
            ForbiddenCapabilities = agent.ForbiddenCapabilities?.ToArray() ?? []
        };

    private static AgentToolRequestInput ValidInput() =>
        new()
        {
            InputId = "input-1",
            RefType = "WorkspaceReport",
            RefId = "workspace-report-1",
            Source = "test",
            Summary = "Sanitised input summary.",
            EvidenceRefs = ["evidence-1"],
            IsSanitised = true
        };

    private static AgentToolRequestEvidence ValidEvidence() =>
        new()
        {
            EvidenceId = "evidence-1",
            RefType = "SourceReport",
            RefId = "source-report-1",
            Summary = "Evidence supports requesting this tool.",
            SupportsNeedForTool = true
        };

    private static AgentToolRequestApprovalRequirement GateOnlyApproval() =>
        new()
        {
            RequiresGovernanceGate = true,
            Reason = "Build/test execution requires gate metadata."
        };

    private static AgentToolRequestApprovalRequirement SourceMutationApproval() =>
        new()
        {
            RequiresHumanApproval = true,
            RequiresGovernanceGate = true,
            RequiresPolicyApproval = true,
            RequiresDryRunFirst = true,
            Reason = "Source mutation requests require human approval, gate, policy, and dry-run metadata."
        };

    private static AgentToolRequestApprovalRequirement ExternalEffectApproval() =>
        new()
        {
            RequiresHumanApproval = true,
            RequiresGovernanceGate = true,
            RequiresPolicyApproval = true,
            Reason = "External effect requests require human approval, gate, and policy metadata."
        };

    private static AgentToolRequestApprovalRequirement MemoryGovernanceApproval() =>
        new()
        {
            RequiresMemoryGovernance = true,
            Reason = "Memory-backed request requires memory governance metadata."
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

    private static AgentToolExecutionGatePolicyContext TestPolicy() =>
        ReadOnlyPolicy() with
        {
            AllowsToolExecution = true,
            AllowsTestExecution = true
        };

    private static AgentToolExecutionGatePolicyContext PatchProposalPolicy() =>
        ReadOnlyPolicy() with { AllowsPatchProposal = true };

    private static AgentToolExecutionGatePolicyContext SourceMutationPolicy() =>
        ReadOnlyPolicy() with
        {
            AllowsToolExecution = true,
            AllowsSourceMutation = true
        };

    private static AgentToolExecutionGatePolicyContext ExternalEffectPolicy() =>
        ReadOnlyPolicy() with
        {
            AllowsToolExecution = true,
            AllowsExternalEffects = true
        };

    private static AgentToolExecutionGatePolicyContext GitHubSubmissionPolicy() =>
        ExternalEffectPolicy() with { AllowsGitHubSubmission = true };

    private static AgentToolExecutionGateApprovalContext GovernanceApproval() =>
        new()
        {
            HasGovernanceGateApproval = true,
            GovernanceGateDecisionId = "gate-decision-1",
            ApprovalRefs = ["gate-decision-1"]
        };

    private static AgentToolExecutionGateApprovalContext FullSourceApproval() =>
        new()
        {
            HasHumanApproval = true,
            HumanApprovalId = "human-approval-1",
            HasPolicyApproval = true,
            PolicyApprovalId = "policy-approval-1",
            HasGovernanceGateApproval = true,
            GovernanceGateDecisionId = "gate-decision-1",
            HasDryRunEvidence = true,
            DryRunEvidenceId = "dry-run-1",
            ApprovalRefs = ["human-approval-1", "policy-approval-1", "gate-decision-1", "dry-run-1"]
        };

    private static AgentToolExecutionGateApprovalContext FullExternalApproval() =>
        new()
        {
            HasHumanApproval = true,
            HumanApprovalId = "human-approval-1",
            HasPolicyApproval = true,
            PolicyApprovalId = "policy-approval-1",
            HasGovernanceGateApproval = true,
            GovernanceGateDecisionId = "gate-decision-1",
            ApprovalRefs = ["human-approval-1", "policy-approval-1", "gate-decision-1"]
        };

    private static AgentToolExecutionGateMemoryContext MemoryAllowed() =>
        new()
        {
            RequestReferencesMemory = true,
            HasMemoryGovernanceDecision = true,
            MemoryGovernanceDecisionId = "memory-governance-1",
            MemoryGovernanceAllowsUse = true,
            MemoryRefs = ["memory-governance-1"]
        };

    private static AgentToolExecutionGateMemoryContext MemoryBlocked() =>
        MemoryAllowed() with { MemoryGovernanceBlocksUse = true, MemoryGovernanceAllowsUse = false };

    private static AgentToolExecutionGateMemoryContext MemoryWarnOnly() =>
        MemoryAllowed() with { MemoryGovernanceAllowsUse = false, MemoryGovernanceWarnsOnly = true };

    private static AgentToolExecutionGateMemoryContext MemoryWarnAndAllow() =>
        MemoryAllowed() with { MemoryGovernanceWarnsOnly = true };

    private static AgentDefinition BuildExternalAgentDefinition() =>
        new()
        {
            AgentId = "test.external-effect-agent",
            Name = "ExternalEffectTestAgent",
            Kind = AgentKind.ImplementationAgent,
            ExecutionMode = AgentExecutionMode.ExternalEffect,
            Purpose = "Test-only external effect requester definition.",
            DefaultModelProfile = "test-only",
            Capabilities = new HashSet<AgentCapability>
            {
                AgentCapability.CallExternalSystem,
                AgentCapability.CreateReport
            },
            ForbiddenCapabilities = new HashSet<AgentCapability>()
        };

    private static AgentToolExecutionGateDecision AssertDecision(
        AgentToolExecutionGateResult result,
        AgentToolExecutionGateDecisionType expected)
    {
        Assert.IsTrue(result.Succeeded, FormatIssues(result.Issues));
        Assert.IsNotNull(result.Decision, "Gate result should include a decision.");
        Assert.AreEqual(expected, result.Decision.Decision);
        return result.Decision;
    }

    private static void AssertHasIssue(AgentToolExecutionGateDecision decision, string code) =>
        Assert.IsTrue(
            decision.Issues.Any(issue => string.Equals(issue.Code, code, StringComparison.Ordinal)),
            $"Expected issue {code}.{Environment.NewLine}{FormatIssues(decision.Issues)}");

    private static void AssertHasReason(AgentToolExecutionGateDecision decision, string code) =>
        Assert.IsTrue(
            decision.Reasons.Any(reason => string.Equals(reason.Code, code, StringComparison.Ordinal)),
            $"Expected reason {code}.{Environment.NewLine}{FormatReasons(decision.Reasons)}");

    private static void AssertFutureExecutorOnly(AgentToolExecutionGateDecision decision)
    {
        Assert.IsTrue(decision.GrantsExecution);
        Assert.IsTrue(decision.RequiresExecutor);
        AssertNoActiveEffects(decision);
    }

    private static void AssertNoFutureExecutor(AgentToolExecutionGateDecision decision)
    {
        Assert.IsFalse(decision.GrantsExecution);
        Assert.IsFalse(decision.RequiresExecutor);
        AssertNoActiveEffects(decision);
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

    private static string FormatIssues(IReadOnlyList<AgentToolExecutionGateIssue> issues) =>
        string.Join(Environment.NewLine, issues.Select(issue => $"{issue.Code}: {issue.Message}"));

    private static string FormatReasons(IReadOnlyList<AgentToolExecutionGateReason> reasons) =>
        string.Join(Environment.NewLine, reasons.Select(reason => $"{reason.Code}: {reason.Message}"));

    private static string ReadRepositoryFile(params string[] pathParts) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), Path.Combine(pathParts)));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;

        if (directory is null)
            throw new InvalidOperationException("Could not locate repository root.");

        return directory.FullName;
    }
}
