using IronDev.Core.Agents;
using IronDev.Core.Agents.Concrete;

namespace IronDev.IntegrationTests.Agents;

internal static class BackendAgentToolRequestFixtures
{
    public static readonly DateTimeOffset DefaultRequestedAtUtc = new(2026, 6, 11, 13, 0, 0, TimeSpan.Zero);
    public static readonly DateTimeOffset DefaultGateEvaluatedAtUtc = new(2026, 6, 11, 12, 55, 0, TimeSpan.Zero);

    public static AgentToolRequest TestingAgentTestRunRequest(DateTimeOffset? requestedAtUtc = null) =>
        new()
        {
            ToolRequestId = "tool-request-test-run-1",
            Status = AgentToolRequestStatus.PendingGate,
            RequestType = AgentToolRequestType.TestExecutionRequest,
            ToolKind = AgentToolKind.TestRun,
            RiskLevel = AgentToolRiskLevel.Medium,
            Scope = StandardRunScope("agent-run-1"),
            Actor = ActorFor(AgentDefinitionCatalog.TestingAgent),
            Purpose = "Run the controlled scripted test plan.",
            Inputs =
            [
                new AgentToolRequestInput
                {
                    InputId = "input-test-plan-1",
                    RefType = "TestPlanRef",
                    RefId = "test-plan-1",
                    Source = "test",
                    Summary = "Sanitised test plan reference.",
                    EvidenceRefs = ["evidence-test-plan-1"],
                    IsSanitised = true
                }
            ],
            Evidence =
            [
                new AgentToolRequestEvidence
                {
                    EvidenceId = "evidence-test-plan-1",
                    RefType = "RunReport",
                    RefId = "run-report-1",
                    Summary = "Evidence supports requesting this test run.",
                    SupportsNeedForTool = true
                }
            ],
            ApprovalRequirement = new AgentToolRequestApprovalRequirement
            {
                RequiresGovernanceGate = true,
                Reason = "Test execution requires gate metadata."
            },
            RequestedAtUtc = requestedAtUtc ?? DefaultRequestedAtUtc
        };

    public static AgentToolRequest ImplementationAgentPatchProposalRequest(DateTimeOffset? requestedAtUtc = null) =>
        new()
        {
            ToolRequestId = "tool-request-patch-proposal-1",
            Status = AgentToolRequestStatus.PendingGate,
            RequestType = AgentToolRequestType.PatchProposalRequest,
            ToolKind = AgentToolKind.PatchProposal,
            RiskLevel = AgentToolRiskLevel.Medium,
            Scope = StandardRunScope("agent-run-implementation-1"),
            Actor = ActorFor(AgentDefinitionCatalog.ImplementationAgent),
            Purpose = "Request a proposal-only implementation patch.",
            Inputs =
            [
                new AgentToolRequestInput
                {
                    InputId = "input-issue-1",
                    RefType = "Issue",
                    RefId = "issue-1",
                    Source = "test",
                    Summary = "Sanitised implementation issue.",
                    EvidenceRefs = ["evidence-issue-1"],
                    IsSanitised = true
                }
            ],
            Evidence =
            [
                new AgentToolRequestEvidence
                {
                    EvidenceId = "evidence-issue-1",
                    RefType = "RunReport",
                    RefId = "run-report-1",
                    Summary = "Evidence supports requesting this patch proposal.",
                    SupportsNeedForTool = true
                }
            ],
            RequestedAtUtc = requestedAtUtc ?? DefaultRequestedAtUtc
        };

    public static AgentToolExecutionGateDecision GateAllowedForTesterTestRun(
        AgentToolRequest request,
        DateTimeOffset? evaluatedAtUtc = null) =>
        RequireAllowedGate(
            request,
            new AgentToolExecutionGatePolicyContext
            {
                PolicyKnown = true,
                AllowsToolRequest = true,
                AllowsToolExecution = true,
                AllowsTestExecution = true,
                PolicyRefs = ["policy:test-execution"]
            },
            new AgentToolExecutionGateApprovalContext
            {
                HasGovernanceGateApproval = true,
                GovernanceGateDecisionId = "governance-gate-1",
                ApprovalRefs = ["governance-gate-1"]
            },
            evaluatedAtUtc);

    public static AgentToolExecutionGateDecision GateAllowedForPatchProposal(
        AgentToolRequest request,
        DateTimeOffset? evaluatedAtUtc = null) =>
        RequireAllowedGate(
            request,
            new AgentToolExecutionGatePolicyContext
            {
                PolicyKnown = true,
                AllowsToolRequest = true,
                AllowsPatchProposal = true,
                PolicyRefs = ["policy:patch-proposal"]
            },
            new AgentToolExecutionGateApprovalContext(),
            evaluatedAtUtc);

    public static AgentToolRequestActor ActorFor(AgentDefinition agent) =>
        new()
        {
            AgentId = agent.AgentId,
            AgentName = agent.Name,
            AgentKind = agent.Kind,
            ExecutionMode = agent.ExecutionMode,
            DeclaredCapabilities = agent.Capabilities?.ToArray() ?? [],
            ForbiddenCapabilities = agent.ForbiddenCapabilities?.ToArray() ?? []
        };

    private static AgentToolRequestScope StandardRunScope(string agentRunId) =>
        new()
        {
            TenantId = "tenant-1",
            ProjectId = "project-1",
            CampaignId = "campaign-1",
            RunId = "run-1",
            AgentRunId = agentRunId,
            CorrelationId = "correlation-1"
        };

    private static AgentToolExecutionGateDecision RequireAllowedGate(
        AgentToolRequest request,
        AgentToolExecutionGatePolicyContext policy,
        AgentToolExecutionGateApprovalContext approval,
        DateTimeOffset? evaluatedAtUtc)
    {
        var result = new AgentToolExecutionGate().Evaluate(new AgentToolExecutionGateRequest
        {
            ToolRequest = request,
            PolicyContext = policy,
            ApprovalContext = approval,
            EvaluatedAtUtc = evaluatedAtUtc ?? DefaultGateEvaluatedAtUtc
        });

        if (!result.Succeeded || result.Decision is null || result.Decision.Decision != AgentToolExecutionGateDecisionType.Allowed)
        {
            var issues = result.Decision?.Issues.Select(issue => $"{issue.Code}: {issue.Message}") ?? result.Issues.Select(issue => $"{issue.Code}: {issue.Message}");
            throw new InvalidOperationException($"Expected an allowed gate decision for {request.ToolKind}.{Environment.NewLine}{string.Join(Environment.NewLine, issues)}");
        }

        return result.Decision;
    }
}

internal static class BackendManualToolExecutionFixtures
{
    public static ManualTesterAgentToolExecutionRequest TesterExecutionRequestWithGovernanceGateApproval(
        DateTimeOffset? requestedAtUtc = null,
        DateTimeOffset? gateEvaluatedAtUtc = null)
    {
        var toolRequest = BackendAgentToolRequestFixtures.TestingAgentTestRunRequest(requestedAtUtc);
        return new ManualTesterAgentToolExecutionRequest
        {
            ManualExecutionId = "manual-test-execution-1",
            ToolRequest = toolRequest,
            GateDecision = BackendAgentToolRequestFixtures.GateAllowedForTesterTestRun(toolRequest, gateEvaluatedAtUtc),
            RequestedByUserId = "user-1",
            TestPlanRef = "test-plan-1",
            TestPlanPath = "tools/dogfood/test-agent-plans/irondev-code-standards-alpha.json",
            WorkingDirectory = "workspace/run-1",
            Parameters = new Dictionary<string, string> { ["filter"] = "IronDevCliTests" },
            RequestedAtUtc = requestedAtUtc ?? BackendAgentToolRequestFixtures.DefaultRequestedAtUtc
        };
    }

    public static ManualImplementationPatchProposalRequest PatchProposalRequestThatDoesNotApplySource(
        DateTimeOffset? requestedAtUtc = null,
        DateTimeOffset? gateEvaluatedAtUtc = null)
    {
        var toolRequest = BackendAgentToolRequestFixtures.ImplementationAgentPatchProposalRequest(requestedAtUtc);
        return new ManualImplementationPatchProposalRequest
        {
            ManualProposalId = "manual-implementation-proposal-1",
            ToolRequest = toolRequest,
            GateDecision = BackendAgentToolRequestFixtures.GateAllowedForPatchProposal(toolRequest, gateEvaluatedAtUtc),
            RequestedByUserId = "user-1",
            ProposalGoal = "Draft a safe proposal for the implementation issue.",
            Inputs = [SanitisedIssuePatchProposalInput()],
            Parameters = new Dictionary<string, string> { ["scope"] = "single-file" },
            RequestedAtUtc = requestedAtUtc ?? BackendAgentToolRequestFixtures.DefaultRequestedAtUtc
        };
    }

    public static PatchProposalInputRef SanitisedIssuePatchProposalInput() =>
        new()
        {
            InputRefId = "proposal-input-1",
            RefType = "Issue",
            RefId = "issue-1",
            Source = "test",
            Summary = "Sanitised implementation issue.",
            EvidenceRefs = ["evidence-issue-1"],
            IsSanitised = true
        };

    public static ScriptedTestRunPlanExecutor ScriptedTestExecutorSucceedsWithEvidence() =>
        new(request => new TestRunPlanExecutionResult
        {
            Succeeded = true,
            ExecutionId = request.ExecutionId,
            ExitCode = 0,
            Summary = "Scripted test-plan executor completed successfully.",
            Outcome = "passed",
            TestsPassed = 9,
            TestsFailed = 0,
            TestsSkipped = 1,
            Duration = TimeSpan.FromSeconds(2),
            EvidenceRefs = ["test-result-1", request.TestPlanRef]
        });

    public static ScriptedPatchProposalGenerator ScriptedPatchProposalGeneratorReturnsProposalOnlyPackage() =>
        new(_ => new PatchProposalGenerationResult
        {
            Succeeded = true,
            Summary = "Scripted patch proposal generator produced proposal-only evidence.",
            Proposal = PatchProposalPackageThatDoesNotApply(),
            EvidenceRefs = ["evidence-package-1"]
        });

    public static ManualTesterAgentToolExecutionOutput TesterOutputWithoutDangerousFlags() =>
        new()
        {
            OutputId = "output-1",
            Summary = "Safe test output.",
            ExitCode = 0,
            Outcome = "passed",
            TestsPassed = 1,
            Duration = TimeSpan.FromMilliseconds(1),
            EvidenceRefs = ["evidence-1"]
        };

    public static ManualImplementationPatchProposalOutput PatchProposalOutputThatDoesNotApplySource() =>
        new()
        {
            OutputId = "output-1",
            Proposal = PatchProposalPackageThatDoesNotApply(),
            Summary = "Safe proposal output.",
            EvidenceRefs = ["evidence-1"]
        };

    public static PatchProposalPackage PatchProposalPackageThatDoesNotApply() =>
        new()
        {
            PatchProposalId = "patch-proposal-1",
            Title = "Safe proposal",
            Summary = "Proposal-only implementation package.",
            Rationale = "Evidence supports a human-reviewed proposal.",
            EvidenceRefs = ["evidence-package-1"],
            IsProposalOnly = true,
            RequiresHumanReview = true,
            RequiresValidation = true,
            AppliesCleanlyClaimed = false,
            CreatesAuthority = false,
            CreatesRuntimeAction = false,
            MutatesSource = false,
            AppliesPatch = false,
            FileChanges = [ProposedFileChangeThatDoesNotWrite()]
        };

    public static ProposedFileChange ProposedFileChangeThatDoesNotWrite() =>
        new()
        {
            FileChangeId = "file-change-1",
            Path = "src/example.cs",
            ChangeKind = "Modify",
            Summary = "Describe a proposed file change.",
            EvidenceRefs = ["evidence-file-1"],
            IsProposalOnly = true,
            WritesFile = false,
            DeletesFile = false,
            AppliesPatch = false,
            Hunks = [ProposedPatchHunkThatIsNotApplied()]
        };

    public static ProposedPatchHunk ProposedPatchHunkThatIsNotApplied() =>
        new()
        {
            HunkId = "hunk-1",
            Summary = "Describe a proposed hunk.",
            BeforeSnippet = "before",
            AfterSnippet = "after",
            EvidenceRefs = ["evidence-hunk-1"],
            ContainsRawPrivateReasoning = false,
            ContainsSecret = false,
            ClaimsApplied = false
        };
}
