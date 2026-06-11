using IronDev.Core.Agents;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Agents;

[TestClass]
public sealed class AgentToolRequestContractTests
{
    private static readonly AgentToolRequestValidator DefaultValidator = new();
    private static readonly DateTimeOffset RequestedAt = new(2026, 6, 11, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void AgentToolRequestContracts_ExposeExpectedCoreTypes()
    {
        Assert.IsNotNull(typeof(AgentToolRequest));
        Assert.IsNotNull(typeof(AgentToolRequestStatus));
        Assert.IsNotNull(typeof(AgentToolRequestType));
        Assert.IsNotNull(typeof(AgentToolKind));
        Assert.IsNotNull(typeof(AgentToolRiskLevel));
        Assert.IsNotNull(typeof(AgentToolRequestScope));
        Assert.IsNotNull(typeof(AgentToolRequestActor));
        Assert.IsNotNull(typeof(AgentToolRequestInput));
        Assert.IsNotNull(typeof(AgentToolRequestEvidence));
        Assert.IsNotNull(typeof(AgentToolRequestApprovalRequirement));
        Assert.IsNotNull(typeof(AgentToolRequestPolicySnapshot));
        Assert.IsNotNull(typeof(AgentToolRequestValidator));
        Assert.IsNotNull(typeof(AgentToolRequestValidationResult));
        Assert.IsNotNull(typeof(AgentToolRequestValidationIssue));
    }

    [TestMethod]
    public void AgentToolRequestStatus_DoesNotExposeExecutionOrApprovalStates()
    {
        var names = Enum.GetNames<AgentToolRequestStatus>();

        CollectionAssert.AreEquivalent(
            new[] { "Draft", "PendingGate", "Rejected", "Cancelled" },
            names);

        var forbidden = new[] { "Approved", "Executing", "Executed", "Completed", "Succeeded", "Failed" };
        foreach (var name in forbidden)
            Assert.IsFalse(names.Contains(name, StringComparer.Ordinal), $"Forbidden status was exposed: {name}");
    }

    [TestMethod]
    public void AgentToolRequest_ValidReadOnlyWorkspaceDiffRequest_Validates()
    {
        var request = BuildRequest(
            AgentDefinitionCatalog.ReportingAgent,
            AgentToolRequestType.ReadOnlyInspection,
            AgentToolKind.WorkspaceDiff,
            AgentToolRiskLevel.Low);

        AssertValid(DefaultValidator.Validate(request));
    }

    [TestMethod]
    public void AgentToolRequest_ValidCodeStandardsAnalysePatchRequest_Validates()
    {
        var request = BuildRequest(
            AgentDefinitionCatalog.IndependentCriticAgent,
            AgentToolRequestType.AnalyseOnly,
            AgentToolKind.CodeStandardsAnalysePatch,
            AgentToolRiskLevel.Low);

        AssertValid(DefaultValidator.Validate(request));
    }

    [TestMethod]
    public void AgentToolRequest_ValidBuildRequest_ValidatesWithGovernanceGate()
    {
        var request = BuildRequest(
            AgentDefinitionCatalog.TestingAgent,
            AgentToolRequestType.BuildExecutionRequest,
            AgentToolKind.BuildRun,
            AgentToolRiskLevel.Medium,
            approval: GateOnlyApproval());

        AssertValid(DefaultValidator.Validate(request));
    }

    [TestMethod]
    public void AgentToolRequest_ValidTestRequest_ValidatesWithGovernanceGate()
    {
        var request = BuildRequest(
            AgentDefinitionCatalog.TestingAgent,
            AgentToolRequestType.TestExecutionRequest,
            AgentToolKind.TestRun,
            AgentToolRiskLevel.Medium,
            approval: GateOnlyApproval());

        AssertValid(DefaultValidator.Validate(request));
    }

    [TestMethod]
    public void AgentToolRequest_ValidPatchProposalRequest_Validates()
    {
        var request = BuildRequest(
            AgentDefinitionCatalog.ImplementationAgent,
            AgentToolRequestType.PatchProposalRequest,
            AgentToolKind.PatchProposal,
            AgentToolRiskLevel.Medium);

        AssertValid(DefaultValidator.Validate(request));
    }

    [TestMethod]
    public void AgentToolRequest_SourceMutationRequest_ValidatesOnlyWithHumanApprovalGatePolicyAndDryRun()
    {
        var request = BuildRequest(
            AgentDefinitionCatalog.ImplementationAgent,
            AgentToolRequestType.SourceMutationRequest,
            AgentToolKind.SourceApply,
            AgentToolRiskLevel.Critical,
            approval: SourceMutationApproval());

        AssertValid(DefaultValidator.Validate(request));
    }

    [TestMethod]
    public void AgentToolRequest_ExternalEffectRequest_ValidatesOnlyWithHumanApprovalGateAndPolicy()
    {
        var externalAgent = BuildExternalAgentDefinition();
        var validator = new AgentToolRequestValidator(AgentDefinitionCatalog.All.Concat([externalAgent]).ToArray());
        var request = BuildRequest(
            externalAgent,
            AgentToolRequestType.ExternalEffectRequest,
            AgentToolKind.ExternalHttpCall,
            AgentToolRiskLevel.Critical,
            approval: ExternalEffectApproval());

        AssertValid(validator.Validate(request));
    }

    [TestMethod]
    public void AgentToolRequest_MemoryBackedRequest_ValidatesOnlyWithMemoryGovernance()
    {
        var request = BuildRequest(
            AgentDefinitionCatalog.ReportingAgent,
            AgentToolRequestType.ReadOnlyInspection,
            AgentToolKind.WorkspaceDiff,
            AgentToolRiskLevel.Low,
            input: ValidInput() with { RefType = "AgentLocalMemoryItem" },
            approval: new AgentToolRequestApprovalRequirement { RequiresMemoryGovernance = true, Reason = "Memory-backed evidence." });

        AssertValid(DefaultValidator.Validate(request));
    }

    [TestMethod]
    public void AgentToolRequest_InvalidRequestShapes_AreRejected()
    {
        var cases = new (AgentToolRequest Request, string Code)[]
        {
            (ValidRequest() with { ToolRequestId = string.Empty }, AgentToolRequestValidator.ToolRequestIdRequired),
            (ValidRequest() with { Status = AgentToolRequestStatus.Rejected }, AgentToolRequestValidator.ToolRequestStatusInvalid),
            (ValidRequest() with { ToolKind = AgentToolKind.Unknown }, AgentToolRequestValidator.ToolRequestKindInvalid),
            (ValidRequest() with { RequestType = (AgentToolRequestType)999 }, AgentToolRequestValidator.ToolRequestTypeInvalid),
            (ValidRequest() with { RiskLevel = (AgentToolRiskLevel)999 }, AgentToolRequestValidator.ToolRequestRiskInvalid),
            (ValidRequest() with { Scope = null! }, AgentToolRequestValidator.ToolRequestScopeRequired),
            (ValidRequest() with { Scope = ValidScope() with { TenantId = string.Empty } }, AgentToolRequestValidator.ToolRequestScopeRequired),
            (ValidRequest() with { Scope = ValidScope() with { ProjectId = string.Empty } }, AgentToolRequestValidator.ToolRequestScopeRequired),
            (ValidRequest() with { Scope = ValidScope() with { CorrelationId = string.Empty } }, AgentToolRequestValidator.ToolRequestScopeRequired),
            (ValidRequest() with { Status = AgentToolRequestStatus.PendingGate, Scope = ValidScope() with { RunId = null } }, AgentToolRequestValidator.ToolRequestScopeRequired),
            (ValidRequest() with { Status = AgentToolRequestStatus.PendingGate, Scope = ValidScope() with { AgentRunId = null } }, AgentToolRequestValidator.ToolRequestScopeRequired),
            (ValidRequest() with { Actor = null! }, AgentToolRequestValidator.ToolRequestAgentRequired),
            (ValidRequest() with { Actor = ValidActor(AgentDefinitionCatalog.ReportingAgent) with { AgentId = string.Empty } }, AgentToolRequestValidator.ToolRequestAgentRequired),
            (ValidRequest() with { Actor = ValidActor(AgentDefinitionCatalog.ReportingAgent) with { AgentId = "missing-agent" } }, AgentToolRequestValidator.ToolRequestAgentDefinitionInvalid),
            (ValidRequest() with { Actor = ValidActor(AgentDefinitionCatalog.ReportingAgent) with { AgentName = "WrongName" } }, AgentToolRequestValidator.ToolRequestAgentDefinitionInvalid),
            (BuildRequest(AgentDefinitionCatalog.IndependentCriticAgent, AgentToolRequestType.SourceMutationRequest, AgentToolKind.SourceApply, AgentToolRiskLevel.Critical, approval: SourceMutationApproval()), AgentToolRequestValidator.ToolRequestAgentCapabilityForbidden),
            (BuildRequest(AgentDefinitionCatalog.ImplementationAgent, AgentToolRequestType.SourceMutationRequest, AgentToolKind.SourceApply, AgentToolRiskLevel.Critical, approval: SourceMutationApproval()) with { Actor = ValidActor(AgentDefinitionCatalog.ImplementationAgent) with { ForbiddenCapabilities = [AgentCapability.MutateSource] } }, AgentToolRequestValidator.ToolRequestAgentCapabilityForbidden),
            (ValidRequest() with { Purpose = string.Empty }, AgentToolRequestValidator.ToolRequestPurposeRequired),
            (ValidRequest() with { Inputs = [] }, AgentToolRequestValidator.ToolRequestInputRequired),
            (ValidRequest() with { Inputs = [ValidInput() with { InputId = string.Empty }] }, AgentToolRequestValidator.ToolRequestInputInvalid),
            (ValidRequest() with { Inputs = [ValidInput() with { IsAuthoritativeForAction = true }] }, AgentToolRequestValidator.ToolRequestInputAuthorityBlocked),
            (ValidRequest() with { Inputs = [ValidInput() with { ContainsRawPrivateReasoning = true }] }, AgentToolRequestValidator.ToolRequestInputRawReasoningBlocked),
            (ValidRequest() with { Inputs = [ValidInput() with { ContainsSecret = true, IsSanitised = false, EvidenceRefs = [] }] }, AgentToolRequestValidator.ToolRequestInputSecretBlocked),
            (ValidRequest() with { Evidence = [] }, AgentToolRequestValidator.ToolRequestEvidenceRequired),
            (ValidRequest() with { Evidence = [ValidEvidence() with { SupportsNeedForTool = false }] }, AgentToolRequestValidator.ToolRequestEvidenceInvalid),
            (ValidRequest() with { Evidence = [ValidEvidence() with { EvidenceId = string.Empty }] }, AgentToolRequestValidator.ToolRequestEvidenceInvalid),
            (ValidRequest() with { Evidence = [ValidEvidence() with { IsAuthorityGrant = true }] }, AgentToolRequestValidator.ToolRequestEvidenceAuthorityBlocked),
            (ValidRequest() with { Evidence = [ValidEvidence() with { ContainsRawPrivateReasoning = true }] }, AgentToolRequestValidator.ToolRequestEvidenceRawReasoningBlocked),
            (ValidRequest() with { Evidence = [ValidEvidence() with { ContainsSecret = true }] }, AgentToolRequestValidator.ToolRequestEvidenceSecretBlocked),
            (ValidRequest() with { ContainsRawPrivateReasoning = true }, AgentToolRequestValidator.ToolRequestInputRawReasoningBlocked),
            (ValidRequest() with { ClaimsApproval = true }, AgentToolRequestValidator.ToolRequestApprovalClaimBlocked),
            (ValidRequest() with { ClaimsExecutionPermission = true }, AgentToolRequestValidator.ToolRequestExecutionPermissionClaimBlocked),
            (ValidRequest() with { ContainsExecutionResult = true }, AgentToolRequestValidator.ToolRequestExecutionResultBlocked),
            (ValidRequest() with { IsExecutableWithoutGate = true }, AgentToolRequestValidator.ToolRequestNotExecutable),
            (ValidRequest() with { RequestType = AgentToolRequestType.BuildExecutionRequest }, AgentToolRequestValidator.ToolRequestTypeToolMismatch)
        };

        foreach (var testCase in cases)
            AssertHasIssue(DefaultValidator.Validate(testCase.Request), testCase.Code);
    }

    [TestMethod]
    public void AgentToolRequest_SourceMutationApprovalRequirements_AreEnforced()
    {
        var baseRequest = BuildRequest(
            AgentDefinitionCatalog.ImplementationAgent,
            AgentToolRequestType.SourceMutationRequest,
            AgentToolKind.SourceApply,
            AgentToolRiskLevel.Critical,
            approval: SourceMutationApproval());

        AssertHasIssue(DefaultValidator.Validate(baseRequest with { ApprovalRequirement = SourceMutationApproval() with { RequiresHumanApproval = false } }), AgentToolRequestValidator.ToolRequestHumanApprovalRequired);
        AssertHasIssue(DefaultValidator.Validate(baseRequest with { ApprovalRequirement = SourceMutationApproval() with { RequiresGovernanceGate = false } }), AgentToolRequestValidator.ToolRequestGateRequired);
        AssertHasIssue(DefaultValidator.Validate(baseRequest with { ApprovalRequirement = SourceMutationApproval() with { RequiresPolicyApproval = false } }), AgentToolRequestValidator.ToolRequestPolicyApprovalRequired);
        AssertHasIssue(DefaultValidator.Validate(baseRequest with { ApprovalRequirement = SourceMutationApproval() with { RequiresDryRunFirst = false } }), AgentToolRequestValidator.ToolRequestDryRunRequired);
        AssertHasIssue(DefaultValidator.Validate(baseRequest with { RiskLevel = AgentToolRiskLevel.High }), AgentToolRequestValidator.ToolRequestRiskInvalid);
    }

    [TestMethod]
    public void AgentToolRequest_ExternalEffectApprovalRequirements_AreEnforced()
    {
        var externalAgent = BuildExternalAgentDefinition();
        var validator = new AgentToolRequestValidator(AgentDefinitionCatalog.All.Concat([externalAgent]).ToArray());
        var baseRequest = BuildRequest(
            externalAgent,
            AgentToolRequestType.ExternalEffectRequest,
            AgentToolKind.GitHubReviewSubmission,
            AgentToolRiskLevel.Critical,
            approval: ExternalEffectApproval());

        AssertHasIssue(validator.Validate(baseRequest with { ApprovalRequirement = ExternalEffectApproval() with { RequiresHumanApproval = false } }), AgentToolRequestValidator.ToolRequestHumanApprovalRequired);
        AssertHasIssue(validator.Validate(baseRequest with { ApprovalRequirement = ExternalEffectApproval() with { RequiresGovernanceGate = false } }), AgentToolRequestValidator.ToolRequestGateRequired);
        AssertHasIssue(validator.Validate(baseRequest with { ApprovalRequirement = ExternalEffectApproval() with { RequiresPolicyApproval = false } }), AgentToolRequestValidator.ToolRequestPolicyApprovalRequired);
        AssertHasIssue(validator.Validate(baseRequest with { RiskLevel = AgentToolRiskLevel.High }), AgentToolRequestValidator.ToolRequestRiskInvalid);
    }

    [TestMethod]
    public void AgentToolRequest_BuildAndTestApprovalRequirements_AreEnforced()
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

        AssertHasIssue(DefaultValidator.Validate(build with { ApprovalRequirement = new AgentToolRequestApprovalRequirement() }), AgentToolRequestValidator.ToolRequestGateRequired);
        AssertHasIssue(DefaultValidator.Validate(test with { ApprovalRequirement = new AgentToolRequestApprovalRequirement() }), AgentToolRequestValidator.ToolRequestGateRequired);
        AssertHasIssue(DefaultValidator.Validate(build with { RiskLevel = AgentToolRiskLevel.Low }), AgentToolRequestValidator.ToolRequestRiskInvalid);
    }

    [TestMethod]
    public void AgentToolRequest_MemoryBackedRequestWithoutMemoryGovernance_IsRejected()
    {
        var inputBacked = ValidRequest() with
        {
            Inputs = [ValidInput() with { RefType = "MemoryInfluenceRecord" }]
        };
        var evidenceBacked = ValidRequest() with
        {
            Evidence = [ValidEvidence() with { RefType = "CollectiveMemoryEvidence" }]
        };

        AssertHasIssue(DefaultValidator.Validate(inputBacked), AgentToolRequestValidator.ToolRequestMemoryGovernanceRequired);
        AssertHasIssue(DefaultValidator.Validate(evidenceBacked), AgentToolRequestValidator.ToolRequestMemoryGovernanceRequired);
    }

    [TestMethod]
    public void AgentToolRequest_StaticBoundary_HasNoExecutionPersistenceOrRuntimeTokens()
    {
        var source = ReadRepositoryFile("IronDev.Core", "Agents", "AgentToolRequestModels.cs");
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
            Assert.IsFalse(source.Contains(token, StringComparison.Ordinal), $"Forbidden active token found in AgentToolRequestModels.cs: {token}");
    }

    [TestMethod]
    public void AgentToolRequest_NoRuntimeExecutorOrRouterExists()
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
            "AgentToolExecutor",
            "IAgentToolExecutor",
            "AgentToolRouter",
            "IAgentToolRouter"
        };

        foreach (var file in files)
        {
            foreach (var token in forbidden)
                Assert.IsFalse(file.Contains(token, StringComparison.Ordinal), $"Runtime wiring/executor token found: {token}");
        }
    }

    private static AgentToolRequest ValidRequest() =>
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
            Purpose = "Describe a requested tool action for future gate evaluation.",
            Inputs = [input ?? ValidInput()],
            Evidence = [evidence ?? ValidEvidence()],
            ApprovalRequirement = approval ?? new AgentToolRequestApprovalRequirement(),
            PolicySnapshot = new AgentToolRequestPolicySnapshot
            {
                PolicyKnown = true,
                AllowsToolRequest = true,
                AllowsToolExecution = false,
                AllowsSourceMutation = false,
                AllowsExternalEffects = false,
                AllowsGitHubSubmission = false,
                PolicyRefs = ["policy:test"]
            },
            RequestedAtUtc = RequestedAt
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
            Reason = "Build/test execution requires the future gate."
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

    private static void AssertValid(AgentToolRequestValidationResult result) =>
        Assert.IsTrue(result.IsValid, FormatIssues(result.Issues));

    private static void AssertHasIssue(AgentToolRequestValidationResult result, string code) =>
        Assert.IsTrue(result.Issues.Any(issue => string.Equals(issue.Code, code, StringComparison.Ordinal)), $"Expected {code}.{Environment.NewLine}{FormatIssues(result.Issues)}");

    private static string FormatIssues(IReadOnlyList<AgentToolRequestValidationIssue> issues) =>
        string.Join(Environment.NewLine, issues.Select(issue => $"{issue.Code}: {issue.Message}"));

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
