using IronDev.Core.Agents;
using IronDev.Core.Agents.Concrete;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Agents;

[TestClass]
public sealed class AgentModelAdapterBoundaryTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void AgentModelAdapterBoundary_ContractsExist()
    {
        Assert.AreEqual(nameof(IAgentModelAdapter), typeof(IAgentModelAdapter).Name);
        Assert.AreEqual(nameof(AgentModelRequest), typeof(AgentModelRequest).Name);
        Assert.AreEqual(nameof(AgentModelResponse), typeof(AgentModelResponse).Name);
        Assert.AreEqual(nameof(AgentModelMessage), typeof(AgentModelMessage).Name);
        Assert.AreEqual(nameof(AgentModelProfile), typeof(AgentModelProfile).Name);
        Assert.AreEqual(nameof(AgentModelInvocationAudit), typeof(AgentModelInvocationAudit).Name);
        Assert.AreEqual(nameof(AgentModelAdapterValidator), typeof(AgentModelAdapterValidator).Name);
        Assert.AreEqual(nameof(FakeAgentModelAdapter), typeof(FakeAgentModelAdapter).Name);
        Assert.AreEqual(nameof(ScriptedAgentModelAdapter), typeof(ScriptedAgentModelAdapter).Name);
    }

    [TestMethod]
    public void FakeAgentModelAdapter_SafeCriticRequestSucceedsWithAudit()
    {
        var request = SafeCriticRequest();
        var result = new FakeAgentModelAdapter().Invoke(request, Now);

        Assert.IsTrue(result.Succeeded);
        Assert.IsNotNull(result.Response);
        Assert.IsNotNull(result.Audit);
        Assert.AreEqual(request.RequestId, result.Response.RequestId);
        Assert.AreEqual(request.AgentId, result.Response.AgentId);
        Assert.AreEqual(request.RequestId, result.Audit.RequestId);
        Assert.AreEqual(request.AgentId, result.Audit.AgentId);
        Assert.IsTrue(result.Response.Usage.TotalTokens > 0);
        AssertNoAuthority(result.Response.ClaimedSafetyFlags);
        AssertAuditGrantsNoAuthority(result.Audit);
        Assert.AreEqual(AgentModelProviderKind.Fake, result.Response.ProviderKind);
    }

    [TestMethod]
    public void FakeAgentModelAdapter_SafeMemoryImprovementRequestSucceedsWithAudit()
    {
        var request = SafeMemoryRequest();
        var result = new FakeAgentModelAdapter().Invoke(request, Now);

        Assert.IsTrue(result.Succeeded);
        Assert.IsNotNull(result.Response);
        Assert.IsNotNull(result.Audit);
        Assert.AreEqual(AgentDefinitionCatalog.MemoryImprovementAgent.AgentId, result.Response.AgentId);
        Assert.AreEqual(AgentSpecialisationCatalog.RepeatedFailureModeDetector.SpecialisationId, result.Response.SpecialisationId);
        AssertNoAuthority(result.Response.ClaimedSafetyFlags);
        AssertAuditGrantsNoAuthority(result.Audit);
    }

    [TestMethod]
    public void AgentModelAdapterValidator_RejectsUnsafeRequests()
    {
        var validator = new AgentModelAdapterValidator(new AgentModelAdapterOptions { MaxContextRefs = 2 });
        var cases = new (AgentModelRequest Request, string Code)[]
        {
            (SafeCriticRequest() with { TenantId = string.Empty }, AgentModelAdapterValidator.ModelRequestScopeRequired),
            (SafeCriticRequest() with { ProjectId = string.Empty }, AgentModelAdapterValidator.ModelRequestScopeRequired),
            (SafeCriticRequest() with { AgentId = string.Empty }, AgentModelAdapterValidator.ModelRequestAgentRequired),
            (SafeCriticRequest() with { Profile = null! }, AgentModelAdapterValidator.ModelProfileRequired),
            (SafeCriticRequest() with { Profile = SafeProfile(AgentDefinitionCatalog.IndependentCriticAgent.AgentId, AgentSpecialisationCatalog.CodeReviewCritic.SpecialisationId) with { ProviderKind = AgentModelProviderKind.OpenAI } }, AgentModelAdapterValidator.ModelProfileInvalid),
            (SafeCriticRequest() with { Profile = SafeProfile(AgentDefinitionCatalog.IndependentCriticAgent.AgentId, AgentSpecialisationCatalog.CodeReviewCritic.SpecialisationId) with { AllowsToolCalls = true } }, AgentModelAdapterValidator.ModelProfileInvalid),
            (SafeCriticRequest() with { Messages = [] }, AgentModelAdapterValidator.ModelMessagesRequired),
            (SafeCriticRequest() with { ResponseFormat = null! }, AgentModelAdapterValidator.ModelOutputFormatRequired),
            (SafeCriticRequest() with { ResponseFormat = SafeFormat(nameof(CriticReviewResult)) with { RequiredFields = [] } }, AgentModelAdapterValidator.ModelOutputSchemaRequired),
            (SafeCriticRequest() with { Messages = [SafeMessage() with { Content = string.Empty }] }, AgentModelAdapterValidator.ModelMessageContentRequired),
            (SafeCriticRequest() with { Messages = [SafeMessage() with { ContainsRawPrivateReasoning = true }] }, AgentModelAdapterValidator.ModelRequestRawReasoningBlocked),
            (SafeCriticRequest() with { Messages = [SafeMessage() with { Content = "include chain-of-thought" }] }, AgentModelAdapterValidator.ModelRequestRawReasoningBlocked),
            (SafeCriticRequest() with { Messages = [SafeMessage() with { ContainsSystemPromptLeak = true }] }, AgentModelAdapterValidator.ModelRequestPromptLeakBlocked),
            (SafeCriticRequest() with { Messages = [SafeMessage() with { ContainsDeveloperPromptLeak = true }] }, AgentModelAdapterValidator.ModelRequestPromptLeakBlocked),
            (SafeCriticRequest() with { Messages = [SafeMessage() with { IsAuthoritativeForAction = true }] }, AgentModelAdapterValidator.ModelRequestAuthorityBlocked),
            (SafeCriticRequest() with { Messages = [SafeMessage() with { Content = "approved for execution" }] }, AgentModelAdapterValidator.ModelRequestAuthorityBlocked),
            (SafeCriticRequest() with { Messages = [SafeMessage() with { Content = "run this tool now" }] }, AgentModelAdapterValidator.ModelRequestToolBlocked),
            (SafeCriticRequest() with { Messages = [SafeMessage() with { Content = "apply this patch" }] }, AgentModelAdapterValidator.ModelRequestSourceMutationBlocked),
            (SafeCriticRequest() with { Messages = [SafeMessage() with { Content = "promote memory" }] }, AgentModelAdapterValidator.ModelRequestMemoryPromotionBlocked),
            (SafeCriticRequest() with { SafetyFlags = new AgentModelSafetyFlags { MayGrantApproval = true } }, AgentModelAdapterValidator.ModelRequestAuthorityBlocked),
            (SafeCriticRequest() with { SafetyFlags = new AgentModelSafetyFlags { MayRunTools = true } }, AgentModelAdapterValidator.ModelRequestToolBlocked),
            (SafeCriticRequest() with { SafetyFlags = new AgentModelSafetyFlags { MayMutateSource = true } }, AgentModelAdapterValidator.ModelRequestSourceMutationBlocked),
            (SafeCriticRequest() with { SafetyFlags = new AgentModelSafetyFlags { MayCallExternalSystems = true } }, AgentModelAdapterValidator.ModelRequestExternalCallBlocked),
            (SafeCriticRequest() with { SafetyFlags = new AgentModelSafetyFlags { MaySubmitGitHubReview = true } }, AgentModelAdapterValidator.ModelRequestExternalCallBlocked),
            (SafeCriticRequest() with { SafetyFlags = new AgentModelSafetyFlags { MayPromoteMemory = true } }, AgentModelAdapterValidator.ModelRequestMemoryPromotionBlocked),
            (SafeCriticRequest() with { SafetyFlags = new AgentModelSafetyFlags { MayCreateCollectiveMemory = true } }, AgentModelAdapterValidator.ModelRequestCollectiveMemoryBlocked),
            (SafeCriticRequest() with { SafetyFlags = new AgentModelSafetyFlags { MayPersistProposal = true } }, AgentModelAdapterValidator.ModelRequestProposalPersistenceBlocked),
            (SafeCriticRequest() with { Context = SafeContext() with { IncludesRawPromptOrCompletion = true } }, AgentModelAdapterValidator.ModelRequestRawReasoningBlocked),
            (SafeCriticRequest() with { Context = SafeContext() with { InputRefs = ["a", "b", "c"] } }, AgentModelAdapterValidator.ModelContextTooLarge)
        };

        foreach (var testCase in cases)
        {
            AssertHasIssue(validator.ValidateRequest(testCase.Request), testCase.Code);
        }
    }

    [TestMethod]
    public void AgentModelAdapterValidator_RejectsUnsafeResponses()
    {
        var validator = new AgentModelAdapterValidator();
        var request = SafeCriticRequest();
        var cases = new (AgentModelResponse Response, string Code)[]
        {
            (SafeResponse(request) with { ResponseId = string.Empty }, AgentModelAdapterValidator.ModelResponseIdRequired),
            (SafeResponse(request) with { RequestId = "other-request" }, AgentModelAdapterValidator.ModelResponseRequestMismatch),
            (SafeResponse(request) with { AgentId = AgentDefinitionCatalog.MemoryImprovementAgent.AgentId }, AgentModelAdapterValidator.ModelResponseAgentMismatch),
            (SafeResponse(request) with { Content = string.Empty }, AgentModelAdapterValidator.ModelResponseContentRequired),
            (SafeResponse(request) with { ContainsRawPrivateReasoning = true }, AgentModelAdapterValidator.ModelResponseRawReasoningBlocked),
            (SafeResponse(request) with { Content = "raw prompt text" }, AgentModelAdapterValidator.ModelResponseRawReasoningBlocked),
            (SafeResponse(request) with { ContainsSystemPromptLeak = true }, AgentModelAdapterValidator.ModelResponsePromptLeakBlocked),
            (SafeResponse(request) with { ContainsDeveloperPromptLeak = true }, AgentModelAdapterValidator.ModelResponsePromptLeakBlocked),
            (SafeResponse(request) with { ContainsAuthorityClaim = true }, AgentModelAdapterValidator.ModelResponseAuthorityClaimBlocked),
            (SafeResponse(request) with { Content = "policy cleared" }, AgentModelAdapterValidator.ModelResponseAuthorityClaimBlocked),
            (SafeResponse(request) with { ContainsToolCommand = true }, AgentModelAdapterValidator.ModelResponseToolCommandBlocked),
            (SafeResponse(request) with { Content = "run this tool" }, AgentModelAdapterValidator.ModelResponseToolCommandBlocked),
            (SafeResponse(request) with { ContainsSourceMutationCommand = true }, AgentModelAdapterValidator.ModelResponseSourceMutationBlocked),
            (SafeResponse(request) with { Content = "delete file" }, AgentModelAdapterValidator.ModelResponseSourceMutationBlocked),
            (SafeResponse(request) with { ContainsMemoryPromotionCommand = true }, AgentModelAdapterValidator.ModelResponseMemoryPromotionBlocked),
            (SafeResponse(request) with { Content = "accepted memory" }, AgentModelAdapterValidator.ModelResponseMemoryPromotionBlocked),
            (SafeResponse(request) with { ClaimedSafetyFlags = new AgentModelSafetyFlags { MayGrantApproval = true } }, AgentModelAdapterValidator.ModelResponseSafetyFlagBlocked),
            (SafeResponse(request) with { Usage = new AgentModelUsage { InputTokens = -1, OutputTokens = 1 } }, AgentModelAdapterValidator.ModelResponseUsageInvalid),
            (SafeResponse(request) with { Usage = new AgentModelUsage { InputTokens = 1, OutputTokens = -1 } }, AgentModelAdapterValidator.ModelResponseUsageInvalid)
        };

        foreach (var testCase in cases)
        {
            AssertHasIssue(validator.ValidateResponse(request, testCase.Response), testCase.Code);
        }
    }

    [TestMethod]
    public void AgentModelAdapterValidator_RejectsUnsafeAuditRecords()
    {
        var validator = new AgentModelAdapterValidator();
        var request = SafeCriticRequest();
        var cases = new (AgentModelInvocationAudit Audit, string Code)[]
        {
            (SafeAudit(request) with { AuditId = string.Empty }, AgentModelAdapterValidator.ModelAuditIdRequired),
            (SafeAudit(request) with { RequestId = string.Empty }, AgentModelAdapterValidator.ModelAuditRequestIdRequired),
            (SafeAudit(request) with { AgentId = string.Empty }, AgentModelAdapterValidator.ModelAuditAgentRequired),
            (SafeAudit(request) with { ProfileId = string.Empty }, AgentModelAdapterValidator.ModelAuditProfileRequired),
            (SafeAudit(request) with { ContainsPromptLeak = true }, AgentModelAdapterValidator.ModelAuditPromptLeakBlocked),
            (SafeAudit(request) with { ContainsRawPrivateReasoning = true }, AgentModelAdapterValidator.ModelAuditRawReasoningBlocked),
            (SafeAudit(request) with { ContainsAuthorityClaim = true }, AgentModelAdapterValidator.ModelAuditAuthorityBlocked),
            (SafeAudit(request) with { GrantsApproval = true }, AgentModelAdapterValidator.ModelAuditAuthorityBlocked),
            (SafeAudit(request) with { GrantsAuthority = true }, AgentModelAdapterValidator.ModelAuditAuthorityBlocked),
            (SafeAudit(request) with { ContainsToolCommand = true }, AgentModelAdapterValidator.ModelAuditToolCommandBlocked),
            (SafeAudit(request) with { ContainsSourceMutationCommand = true }, AgentModelAdapterValidator.ModelAuditSourceMutationBlocked),
            (SafeAudit(request) with { ContainsMemoryPromotionCommand = true }, AgentModelAdapterValidator.ModelAuditMemoryPromotionBlocked),
            (SafeAudit(request) with { GrantsMemoryPromotion = true }, AgentModelAdapterValidator.ModelAuditMemoryPromotionBlocked),
            (SafeAudit(request) with { Usage = new AgentModelUsage { InputTokens = -1, OutputTokens = 1 } }, AgentModelAdapterValidator.ModelAuditUsageInvalid)
        };

        foreach (var testCase in cases)
        {
            AssertHasIssue(validator.ValidateAudit(testCase.Audit), testCase.Code);
        }
    }

    [TestMethod]
    public void AgentModelAdapterValidator_EnforcesSpecialisationCompatibility()
    {
        var validator = new AgentModelAdapterValidator();

        AssertNoIssue(validator.ValidateRequest(SafeCriticRequest(AgentSpecialisationCatalog.CodeReviewCritic.SpecialisationId)), AgentModelAdapterValidator.ModelSpecialisationIncompatible);
        AssertNoIssue(validator.ValidateRequest(SafeCriticRequest(AgentSpecialisationCatalog.SecurityCritic.SpecialisationId)), AgentModelAdapterValidator.ModelSpecialisationIncompatible);
        AssertNoIssue(validator.ValidateRequest(SafeMemoryRequest(AgentSpecialisationCatalog.RepeatedFailureModeDetector.SpecialisationId)), AgentModelAdapterValidator.ModelSpecialisationIncompatible);
        AssertNoIssue(validator.ValidateRequest(SafeMemoryRequest(AgentSpecialisationCatalog.StaleMemoryDetector.SpecialisationId)), AgentModelAdapterValidator.ModelSpecialisationIncompatible);

        AssertHasIssue(
            validator.ValidateRequest(SafeCriticRequest(AgentSpecialisationCatalog.RepeatedFailureModeDetector.SpecialisationId)),
            AgentModelAdapterValidator.ModelSpecialisationIncompatible);
        AssertHasIssue(
            validator.ValidateRequest(SafeMemoryRequest(AgentSpecialisationCatalog.CodeReviewCritic.SpecialisationId)),
            AgentModelAdapterValidator.ModelSpecialisationIncompatible);
        AssertHasIssue(
            validator.ValidateRequest(SafeCriticRequest("builtin.critic.unknown")),
            AgentModelAdapterValidator.ModelSpecialisationIncompatible);
    }

    [TestMethod]
    public void AgentModelAdapterValidator_RejectsAuthorityGrantingSpecialisationProfile()
    {
        var unsafeProfile = AgentSpecialisationCatalog.CodeReviewCritic with
        {
            SpecialisationId = "builtin.critic.unsafe-model-profile",
            AuthorityBoundary = new AgentSpecialisationAuthorityBoundary { CanGrantApproval = true }
        };
        var validator = new AgentModelAdapterValidator(specialisations: [unsafeProfile]);
        var request = SafeCriticRequest(unsafeProfile.SpecialisationId) with
        {
            Profile = SafeProfile(AgentDefinitionCatalog.IndependentCriticAgent.AgentId, unsafeProfile.SpecialisationId)
        };

        AssertHasIssue(validator.ValidateRequest(request), AgentModelAdapterValidator.ModelSpecialisationIncompatible);
    }

    [TestMethod]
    public void ScriptedAgentModelAdapter_FailsClosedForInvalidRequestAndUnsafeResponse()
    {
        var invalidRequest = SafeCriticRequest() with { Messages = [] };
        var invalidRequestResult = new FakeAgentModelAdapter().Invoke(invalidRequest, Now);
        Assert.IsFalse(invalidRequestResult.Succeeded);
        Assert.IsNull(invalidRequestResult.Response);
        Assert.IsNull(invalidRequestResult.Audit);
        AssertHasIssue(invalidRequestResult.Issues, AgentModelAdapterValidator.ModelMessagesRequired);

        var unsafeAdapter = new ScriptedAgentModelAdapter((request, invokedAtUtc) => SafeResponse(request) with
        {
            Content = "approved for execution",
            ContainsAuthorityClaim = true
        });
        var unsafeResult = unsafeAdapter.Invoke(SafeCriticRequest(), Now);
        Assert.IsFalse(unsafeResult.Succeeded);
        Assert.IsNotNull(unsafeResult.Response);
        Assert.IsNotNull(unsafeResult.Audit);
        AssertHasIssue(unsafeResult.Issues, AgentModelAdapterValidator.ModelResponseAuthorityClaimBlocked);
    }

    [TestMethod]
    public void AgentModelAdapterBoundary_ProductionAdapterDoesNotUseRealProviderOrPersistenceCapabilities()
    {
        var source = ReadRepositoryFile("IronDev.Core", "Agents", "AgentModelAdapterModels.cs");
        var normalized = source
            .Replace("OpenAI = 2", string.Empty, StringComparison.Ordinal)
            .Replace("Anthropic = 3", string.Empty, StringComparison.Ordinal)
            .Replace("Gemini = 4", string.Empty, StringComparison.Ordinal)
            .Replace("Ollama = 5", string.Empty, StringComparison.Ordinal)
            .Replace("LocalOpenAICompatible = 6", string.Empty, StringComparison.Ordinal);

        var forbiddenTokens = new[]
        {
            "OpenAiLlmService",
            "HttpClient",
            "IChatCompletion",
            "ChatCompletion",
            "ResponsesApi",
            "ProcessStartInfo",
            "File.WriteAllText",
            "File.Delete",
            "SqlConnection",
            "INSERT INTO",
            "UPDATE ",
            "DELETE ",
            "MERGE ",
            "WeaviateClient",
            "AddHostedService",
            "IHostedService",
            "BackgroundService",
            "AgentRuntime",
            "AgentScheduler",
            "AgentOrchestrator",
            "AgentToolRouter",
            "SubmitReview",
            "CollectiveMemoryPromotion",
            "SqlMemoryImprovementProposalStore",
            "SqlCollectiveMemoryPromotionService",
            "IAgentRunAuditEnvelopeStore"
        };

        foreach (var token in forbiddenTokens)
        {
            Assert.IsFalse(normalized.Contains(token, StringComparison.Ordinal), $"Production model adapter boundary must not contain active token '{token}'.");
        }
    }

    [TestMethod]
    public void AgentModelAdapterBoundary_IsNotWiredIntoManualAgentsOrRuntime()
    {
        var files = new[]
        {
            ReadRepositoryFile("IronDev.Core", "Agents", "Concrete", "ManualIndependentCriticAgentService.cs"),
            ReadRepositoryFile("IronDev.Core", "Agents", "Concrete", "ManualMemoryImprovementAgentService.cs"),
            ReadRepositoryFile("IronDev.Core", "Agents", "Concrete", "StoredManualAgentExecutionService.cs"),
            ReadRepositoryFile("IronDev.Api", "Program.cs")
        };

        foreach (var file in files)
        {
            Assert.IsFalse(file.Contains(nameof(IAgentModelAdapter), StringComparison.Ordinal));
            Assert.IsFalse(file.Contains(nameof(FakeAgentModelAdapter), StringComparison.Ordinal));
            Assert.IsFalse(file.Contains(nameof(ScriptedAgentModelAdapter), StringComparison.Ordinal));
            Assert.IsFalse(file.Contains(nameof(AgentModelAdapterValidator), StringComparison.Ordinal));
        }

        var apiFiles = Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "IronDev.Api"), "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToArray();
        foreach (var file in apiFiles)
        {
            Assert.IsFalse(file.Contains(nameof(IAgentModelAdapter), StringComparison.Ordinal));
            Assert.IsFalse(file.Contains("AddHostedService", StringComparison.Ordinal));
            Assert.IsFalse(file.Contains("BackgroundService", StringComparison.Ordinal));
        }
    }

    private static AgentModelRequest SafeCriticRequest(string? specialisationId = null)
    {
        specialisationId ??= AgentSpecialisationCatalog.CodeReviewCritic.SpecialisationId;
        return SafeRequest(
            AgentDefinitionCatalog.IndependentCriticAgent.AgentId,
            specialisationId,
            nameof(CriticReviewResult));
    }

    private static AgentModelRequest SafeMemoryRequest(string? specialisationId = null)
    {
        specialisationId ??= AgentSpecialisationCatalog.RepeatedFailureModeDetector.SpecialisationId;
        return SafeRequest(
            AgentDefinitionCatalog.MemoryImprovementAgent.AgentId,
            specialisationId,
            nameof(MemoryImprovementDetectionResult));
    }

    private static AgentModelRequest SafeRequest(
        string agentId,
        string specialisationId,
        string outputContractName) =>
        new()
        {
            RequestId = "model-request-001",
            TenantId = "tenant-1",
            ProjectId = "project-1",
            CampaignId = "campaign-1",
            AgentRunId = "agent-run-1",
            AgentId = agentId,
            SpecialisationId = specialisationId,
            Profile = SafeProfile(agentId, specialisationId),
            Messages = [SafeMessage()],
            Context = SafeContext(),
            ResponseFormat = SafeFormat(outputContractName),
            CreatedAtUtc = Now
        };

    private static AgentModelProfile SafeProfile(string agentId, string specialisationId) =>
        new()
        {
            ProfileId = "fake-agent-model-profile",
            DisplayName = "Fake Agent Model Profile",
            ProviderKind = AgentModelProviderKind.Fake,
            ModelName = "fake-agent-model",
            IsEnabled = true,
            AllowsToolCalls = false,
            AllowsJsonOutput = true,
            AllowsStreaming = false,
            AllowsExternalNetwork = false,
            MaxInputTokens = 4096,
            MaxOutputTokens = 1024,
            Temperature = 0.1m,
            AllowedAgentIds = [agentId],
            AllowedSpecialisationIds = [specialisationId]
        };

    private static AgentModelMessage SafeMessage() =>
        new()
        {
            MessageId = "message-1",
            Role = AgentModelRole.User,
            Content = "Review supplied evidence and return advisory structured output only.",
            EvidenceRefs = ["evidence-1"]
        };

    private static AgentModelRequestContext SafeContext() =>
        new()
        {
            InputRefs = ["input-1"],
            EvidenceRefs = ["evidence-1"],
            AuditRefs = ["audit-1"]
        };

    private static AgentModelResponseFormat SafeFormat(string outputContractName) =>
        new()
        {
            FormatId = "format-json-contract",
            OutputContractName = outputContractName,
            RequiresJson = true,
            RequiresSchemaValidation = true,
            AllowsFreeText = false,
            RequiredFields = ["summary", "warnings"]
        };

    private static AgentModelResponse SafeResponse(AgentModelRequest request) =>
        new()
        {
            ResponseId = "response-1",
            RequestId = request.RequestId,
            AgentId = request.AgentId,
            SpecialisationId = request.SpecialisationId,
            ProviderKind = request.Profile.ProviderKind,
            ModelName = request.Profile.ModelName,
            Content = "Advisory response candidate only. Human review and governance remain separate.",
            StructuredJson = "{}",
            Usage = new AgentModelUsage { InputTokens = 10, OutputTokens = 12 },
            CompletedAtUtc = Now
        };

    private static AgentModelInvocationAudit SafeAudit(AgentModelRequest request) =>
        new()
        {
            AuditId = "model-audit-1",
            RequestId = request.RequestId,
            AgentId = request.AgentId,
            SpecialisationId = request.SpecialisationId,
            ProfileId = request.Profile.ProfileId,
            ProviderKind = request.Profile.ProviderKind,
            ModelName = request.Profile.ModelName,
            RequestedAtUtc = Now,
            CompletedAtUtc = Now,
            Succeeded = true,
            InputRefs = request.Context.InputRefs,
            EvidenceRefs = request.Context.EvidenceRefs,
            Usage = new AgentModelUsage { InputTokens = 10, OutputTokens = 12 }
        };

    private static void AssertNoAuthority(AgentModelSafetyFlags flags)
    {
        Assert.IsFalse(flags.MayGrantApproval);
        Assert.IsFalse(flags.MayGrantPolicyApproval);
        Assert.IsFalse(flags.MayRepresentHumanApproval);
        Assert.IsFalse(flags.MayPromoteMemory);
        Assert.IsFalse(flags.MayCreateCollectiveMemory);
        Assert.IsFalse(flags.MayRunTools);
        Assert.IsFalse(flags.MayMutateSource);
        Assert.IsFalse(flags.MayCallExternalSystems);
        Assert.IsFalse(flags.MaySubmitGitHubReview);
        Assert.IsFalse(flags.MayPersistProposal);
    }

    private static void AssertAuditGrantsNoAuthority(AgentModelInvocationAudit audit)
    {
        Assert.IsFalse(audit.GrantsApproval);
        Assert.IsFalse(audit.GrantsAuthority);
        Assert.IsFalse(audit.GrantsMemoryPromotion);
        Assert.IsFalse(audit.ContainsAuthorityClaim);
        Assert.IsFalse(audit.ContainsToolCommand);
        Assert.IsFalse(audit.ContainsSourceMutationCommand);
        Assert.IsFalse(audit.ContainsMemoryPromotionCommand);
        Assert.IsFalse(audit.ContainsRawPrivateReasoning);
        Assert.IsFalse(audit.ContainsPromptLeak);
    }

    private static void AssertHasIssue(IReadOnlyList<AgentModelAdapterIssue> issues, string code) =>
        Assert.IsTrue(
            issues.Any(issue => string.Equals(issue.Code, code, StringComparison.Ordinal)),
            $"Expected issue code '{code}' but got: {string.Join(", ", issues.Select(issue => issue.Code))}");

    private static void AssertNoIssue(IReadOnlyList<AgentModelAdapterIssue> issues, string code) =>
        Assert.IsFalse(
            issues.Any(issue => string.Equals(issue.Code, code, StringComparison.Ordinal)),
            $"Did not expect issue code '{code}' but got: {string.Join(", ", issues.Select(issue => issue.Code))}");

    private static string ReadRepositoryFile(params string[] segments) =>
        File.ReadAllText(Path.Combine([RepositoryRoot(), .. segments]));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".git")))
        {
            directory = directory.Parent;
        }

        Assert.IsNotNull(directory, "Could not locate repository root.");
        return directory.FullName;
    }
}


