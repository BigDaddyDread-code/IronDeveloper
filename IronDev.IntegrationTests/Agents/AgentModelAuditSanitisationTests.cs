using IronDev.Core.Agents;
using IronDev.Core.Agents.Concrete;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Agents;

[TestClass]
public sealed class AgentModelAuditSanitisationTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 11, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void AgentModelAuditSanitisation_ContractsExist()
    {
        Assert.AreEqual(nameof(IAgentModelAuditSanitiser), typeof(IAgentModelAuditSanitiser).Name);
        Assert.AreEqual(nameof(AgentModelAuditSanitiser), typeof(AgentModelAuditSanitiser).Name);
        Assert.AreEqual(nameof(AgentModelSanitisationRequest), typeof(AgentModelSanitisationRequest).Name);
        Assert.AreEqual(nameof(AgentModelSanitisationResult), typeof(AgentModelSanitisationResult).Name);
        Assert.AreEqual(nameof(AgentModelSanitisedPrompt), typeof(AgentModelSanitisedPrompt).Name);
        Assert.AreEqual(nameof(AgentModelSanitisedResponse), typeof(AgentModelSanitisedResponse).Name);
        Assert.AreEqual(nameof(AgentModelSanitisedInvocationAudit), typeof(AgentModelSanitisedInvocationAudit).Name);
        Assert.AreEqual(nameof(AgentModelSanitisationStatus), typeof(AgentModelSanitisationStatus).Name);
        Assert.AreEqual(nameof(AgentModelRetainability), typeof(AgentModelRetainability).Name);
        Assert.AreEqual(nameof(AgentModelRedaction), typeof(AgentModelRedaction).Name);
        Assert.AreEqual(nameof(AgentModelRedactionKind), typeof(AgentModelRedactionKind).Name);
        Assert.AreEqual(nameof(AgentModelSanitisationIssue), typeof(AgentModelSanitisationIssue).Name);
    }

    [TestMethod]
    public void AgentModelAuditSanitiser_SafeRequestOnlyReturnsSafeSanitisedPrompt()
    {
        var request = SafeSanitisationRequest(includeResponse: false, includeAudit: false, allowPreview: false);
        var result = new AgentModelAuditSanitiser().Sanitise(request);

        Assert.AreEqual(AgentModelSanitisationStatus.Safe, result.Status);
        Assert.IsNotNull(result.Prompt);
        Assert.IsNull(result.Response);
        Assert.IsNull(result.Audit);
        Assert.AreEqual(AgentModelRetainability.AuditSummaryOnly, result.Prompt.Retainability);
        Assert.IsNull(result.Prompt.RedactedPreview);
        Assert.AreEqual("model-request-001", result.Prompt.RequestId);
        Assert.AreEqual(AgentDefinitionCatalog.IndependentCriticAgent.AgentId, result.Prompt.AgentId);
        Assert.AreEqual(AgentSpecialisationCatalog.CodeReviewCritic.SpecialisationId, result.Prompt.SpecialisationId);
        CollectionAssert.Contains(result.Prompt.InputRefs.ToArray(), "input-1");
        CollectionAssert.Contains(result.Prompt.EvidenceRefs.ToArray(), "evidence-1");
        CollectionAssert.Contains(result.Prompt.MemoryRefs.ToArray(), "memory-1");
        CollectionAssert.Contains(result.Prompt.AuditRefs.ToArray(), "audit-1");
        AssertNoUnsafePromptFlags(result.Prompt);
        Assert.AreEqual(0, result.Redactions.Count);
    }

    [TestMethod]
    public void AgentModelAuditSanitiser_SafeRequestResponseAndAuditReturnSafeObjects()
    {
        var request = SafeSanitisationRequest();
        var result = new AgentModelAuditSanitiser().Sanitise(request);

        Assert.AreEqual(AgentModelSanitisationStatus.Safe, result.Status);
        Assert.IsNotNull(result.Prompt);
        Assert.IsNotNull(result.Response);
        Assert.IsNotNull(result.Audit);
        Assert.AreEqual("response-1", result.Response.ResponseId);
        Assert.AreEqual("model-request-001", result.Response.RequestId);
        Assert.AreEqual(AgentDefinitionCatalog.IndependentCriticAgent.AgentId, result.Response.AgentId);
        Assert.AreEqual(AgentSpecialisationCatalog.CodeReviewCritic.SpecialisationId, result.Response.SpecialisationId);
        Assert.AreEqual(10, result.Response.Usage.InputTokens);
        Assert.AreEqual(12, result.Response.Usage.OutputTokens);
        Assert.AreEqual("model-audit-1", result.Audit.AuditId);
        Assert.AreEqual(AgentModelSanitisationStatus.Safe, result.Audit.SanitisationStatus);
        AssertNoUnsafeResponseFlags(result.Response);
        AssertNoUnsafeAuditFlags(result.Audit);
        Assert.IsTrue(result.Prompt.Summary.Length > 0);
        Assert.IsTrue(result.Response.Summary.Length > 0);
        Assert.IsFalse(result.Prompt.Summary.Contains("Review supplied evidence", StringComparison.Ordinal));
        Assert.IsFalse(result.Response.Summary.Contains("Advisory response candidate", StringComparison.Ordinal));
        Assert.AreEqual(0, result.Redactions.Count);
    }

    [TestMethod]
    public void AgentModelAuditSanitiser_SafeStructuredJsonCandidateRequiresExplicitAllowance()
    {
        var noCandidate = new AgentModelAuditSanitiser().Sanitise(SafeSanitisationRequest(allowStructuredJson: false));
        var withCandidate = new AgentModelAuditSanitiser().Sanitise(SafeSanitisationRequest(allowStructuredJson: true));

        Assert.IsNotNull(noCandidate.Response);
        Assert.IsNotNull(withCandidate.Response);
        Assert.IsNull(noCandidate.Response.StructuredJsonCandidate);
        Assert.AreEqual("{}", withCandidate.Response.StructuredJsonCandidate);
        Assert.AreEqual(AgentModelRetainability.StructuredOutputCandidate, withCandidate.Response.Retainability);
    }

    [TestMethod]
    public void AgentModelAuditSanitiser_RedactedPreviewRequiresExplicitAllowance()
    {
        var noPreview = new AgentModelAuditSanitiser().Sanitise(SafeSanitisationRequest(allowPreview: false));
        var withPreview = new AgentModelAuditSanitiser().Sanitise(SafeSanitisationRequest(allowPreview: true));

        Assert.IsNotNull(noPreview.Prompt);
        Assert.IsNotNull(noPreview.Response);
        Assert.IsNotNull(withPreview.Prompt);
        Assert.IsNotNull(withPreview.Response);
        Assert.IsNull(noPreview.Prompt.RedactedPreview);
        Assert.IsNull(noPreview.Response.RedactedPreview);
        Assert.IsFalse(string.IsNullOrWhiteSpace(withPreview.Prompt.RedactedPreview));
        Assert.IsFalse(string.IsNullOrWhiteSpace(withPreview.Response.RedactedPreview));
    }

    [TestMethod]
    public void AgentModelAuditSanitiser_SecretsAreRedactedWithoutLeakingValues()
    {
        var secretValues = new[]
        {
            "api_key=abc123secret",
            "Authorization: Basic abc123secret",
            "Bearer abc123secret",
            "password=abc123secret",
            "ghp_abc123secret",
            "sk-abc123secret"
        };

        foreach (var secret in secretValues)
        {
            var request = SafeSanitisationRequest(
                modelRequest: SafeRequest() with { Messages = [SafeMessage() with { Content = $"Review evidence with {secret}" }] },
                allowPreview: true);

            var result = new AgentModelAuditSanitiser().Sanitise(request);

            Assert.AreEqual(AgentModelSanitisationStatus.SafeWithRedactions, result.Status, secret);
            Assert.IsNotNull(result.Prompt);
            Assert.IsTrue(result.Redactions.Count > 0);
            Assert.IsTrue(result.Prompt.RedactedPreview?.Contains("[REDACTED]", StringComparison.Ordinal) == true);
            Assert.IsFalse(result.Prompt.RedactedPreview?.Contains("abc123secret", StringComparison.Ordinal) == true);
            Assert.IsFalse(result.Redactions.Any(redaction => redaction.Reason.Contains("abc123secret", StringComparison.Ordinal)));
            Assert.IsTrue(result.Issues.Any(issue => string.Equals(issue.Code, AgentModelAuditSanitiser.SecretMaterialRedacted, StringComparison.Ordinal)));
        }
    }

    [TestMethod]
    public void AgentModelAuditSanitiser_RawPrivateAndPromptLeakMarkersAreRejected()
    {
        var cases = new (AgentModelSanitisationRequest Request, AgentModelRedactionKind Kind)[]
        {
            (RequestWithMessage("raw prompt"), AgentModelRedactionKind.RawPrompt),
            (RequestWithResponse(SafeResponse(SafeRequest()) with { Content = "raw completion" }), AgentModelRedactionKind.RawCompletion),
            (RequestWithMessage("chain-of-thought"), AgentModelRedactionKind.ChainOfThought),
            (RequestWithMessage("scratchpad"), AgentModelRedactionKind.Scratchpad),
            (RequestWithMessage("private reasoning"), AgentModelRedactionKind.PrivateReasoning),
            (RequestWithMessage("hidden reasoning"), AgentModelRedactionKind.HiddenReasoning),
            (RequestWithMessage("system prompt"), AgentModelRedactionKind.SystemPrompt),
            (RequestWithMessage("developer prompt"), AgentModelRedactionKind.DeveloperPrompt),
            (SafeSanitisationRequest(modelRequest: SafeRequest() with { Messages = [SafeMessage() with { ContainsRawPrivateReasoning = true }] }), AgentModelRedactionKind.PrivateReasoning),
            (SafeSanitisationRequest(modelRequest: SafeRequest() with { Context = SafeContext() with { IncludesPrivateReasoning = true } }), AgentModelRedactionKind.PrivateReasoning),
            (SafeSanitisationRequest(modelRequest: SafeRequest() with { Context = SafeContext() with { IncludesRawPromptOrCompletion = true } }), AgentModelRedactionKind.PrivateReasoning),
            (RequestWithResponse(SafeResponse(SafeRequest()) with { ContainsRawPrivateReasoning = true }), AgentModelRedactionKind.PrivateReasoning),
            (RequestWithAudit(SafeAudit(SafeRequest()) with { ContainsRawPrivateReasoning = true }), AgentModelRedactionKind.PrivateReasoning)
        };

        foreach (var testCase in cases)
        {
            AssertRejectedWithoutPreviews(testCase.Request, testCase.Kind);
        }
    }

    [TestMethod]
    public void AgentModelAuditSanitiser_SystemAndDeveloperRoleMessagesAreRejected()
    {
        AssertRejectedWithoutPreviews(
            SafeSanitisationRequest(modelRequest: SafeRequest() with { Messages = [SafeMessage() with { Role = AgentModelRole.System }] }),
            AgentModelRedactionKind.SystemPrompt);
        AssertRejectedWithoutPreviews(
            SafeSanitisationRequest(modelRequest: SafeRequest() with { Messages = [SafeMessage() with { Role = AgentModelRole.Developer }] }),
            AgentModelRedactionKind.DeveloperPrompt);
    }

    [TestMethod]
    public void AgentModelAuditSanitiser_AuthorityAndActionMarkersAreRejected()
    {
        var cases = new (AgentModelSanitisationRequest Request, AgentModelRedactionKind Kind)[]
        {
            (RequestWithMessage("approval granted"), AgentModelRedactionKind.AuthorityClaim),
            (RequestWithMessage("approved for execution"), AgentModelRedactionKind.AuthorityClaim),
            (RequestWithMessage("policy cleared"), AgentModelRedactionKind.AuthorityClaim),
            (RequestWithMessage("human approved"), AgentModelRedactionKind.AuthorityClaim),
            (RequestWithMessage("authoritative for action"), AgentModelRedactionKind.AuthorityClaim),
            (RequestWithMessage("may execute"), AgentModelRedactionKind.AuthorityClaim),
            (RequestWithMessage("can execute"), AgentModelRedactionKind.AuthorityClaim),
            (RequestWithMessage("run this tool"), AgentModelRedactionKind.ToolCommand),
            (RequestWithMessage("apply this patch"), AgentModelRedactionKind.SourceMutationCommand),
            (RequestWithMessage("submit GitHub review"), AgentModelRedactionKind.GitHubReviewCommand),
            (RequestWithMessage("promote memory"), AgentModelRedactionKind.MemoryPromotionCommand),
            (RequestWithMessage("accepted memory"), AgentModelRedactionKind.MemoryPromotionCommand),
            (RequestWithMessage("create CollectiveMemory"), AgentModelRedactionKind.MemoryPromotionCommand),
            (RequestWithMessage("persist proposal"), AgentModelRedactionKind.MemoryPromotionCommand),
            (SafeSanitisationRequest(modelRequest: SafeRequest() with { Messages = [SafeMessage() with { IsAuthoritativeForAction = true }] }), AgentModelRedactionKind.AuthorityClaim),
            (SafeSanitisationRequest(modelRequest: SafeRequest() with { SafetyFlags = new AgentModelSafetyFlags { MayGrantApproval = true } }), AgentModelRedactionKind.AuthorityClaim),
            (SafeSanitisationRequest(modelRequest: SafeRequest() with { SafetyFlags = new AgentModelSafetyFlags { MayRunTools = true } }), AgentModelRedactionKind.ToolCommand),
            (SafeSanitisationRequest(modelRequest: SafeRequest() with { SafetyFlags = new AgentModelSafetyFlags { MayMutateSource = true } }), AgentModelRedactionKind.SourceMutationCommand),
            (SafeSanitisationRequest(modelRequest: SafeRequest() with { SafetyFlags = new AgentModelSafetyFlags { MayCallExternalSystems = true } }), AgentModelRedactionKind.ExternalCallCommand),
            (SafeSanitisationRequest(modelRequest: SafeRequest() with { SafetyFlags = new AgentModelSafetyFlags { MaySubmitGitHubReview = true } }), AgentModelRedactionKind.ExternalCallCommand),
            (SafeSanitisationRequest(modelRequest: SafeRequest() with { SafetyFlags = new AgentModelSafetyFlags { MayPromoteMemory = true } }), AgentModelRedactionKind.MemoryPromotionCommand),
            (SafeSanitisationRequest(response: SafeResponse(SafeRequest()) with { ClaimedSafetyFlags = new AgentModelSafetyFlags { MayGrantApproval = true } }), AgentModelRedactionKind.AuthorityClaim),
            (RequestWithAudit(SafeAudit(SafeRequest()) with { GrantsApproval = true }), AgentModelRedactionKind.AuthorityClaim),
            (RequestWithAudit(SafeAudit(SafeRequest()) with { GrantsAuthority = true }), AgentModelRedactionKind.AuthorityClaim),
            (RequestWithAudit(SafeAudit(SafeRequest()) with { GrantsMemoryPromotion = true }), AgentModelRedactionKind.MemoryPromotionCommand)
        };

        foreach (var testCase in cases)
        {
            AssertRejectedWithoutPreviews(testCase.Request, testCase.Kind);
        }
    }

    [TestMethod]
    public void AgentModelAuditSanitiser_UnsafeResponseAndAuditFlagsAreRejected()
    {
        var cases = new (AgentModelSanitisationRequest Request, AgentModelRedactionKind Kind)[]
        {
            (RequestWithResponse(SafeResponse(SafeRequest()) with { ContainsAuthorityClaim = true }), AgentModelRedactionKind.AuthorityClaim),
            (RequestWithResponse(SafeResponse(SafeRequest()) with { ContainsToolCommand = true }), AgentModelRedactionKind.ToolCommand),
            (RequestWithResponse(SafeResponse(SafeRequest()) with { ContainsSourceMutationCommand = true }), AgentModelRedactionKind.SourceMutationCommand),
            (RequestWithResponse(SafeResponse(SafeRequest()) with { ContainsMemoryPromotionCommand = true }), AgentModelRedactionKind.MemoryPromotionCommand),
            (RequestWithAudit(SafeAudit(SafeRequest()) with { ContainsAuthorityClaim = true }), AgentModelRedactionKind.AuthorityClaim),
            (RequestWithAudit(SafeAudit(SafeRequest()) with { ContainsToolCommand = true }), AgentModelRedactionKind.ToolCommand),
            (RequestWithAudit(SafeAudit(SafeRequest()) with { ContainsSourceMutationCommand = true }), AgentModelRedactionKind.SourceMutationCommand),
            (RequestWithAudit(SafeAudit(SafeRequest()) with { ContainsMemoryPromotionCommand = true }), AgentModelRedactionKind.MemoryPromotionCommand)
        };

        foreach (var testCase in cases)
        {
            AssertRejectedWithoutPreviews(testCase.Request, testCase.Kind);
        }
    }

    [TestMethod]
    public void AgentModelAuditSanitiser_RelationshipToAdapterValidatorIsFailClosed()
    {
        var sanitiser = new AgentModelAuditSanitiser();
        var invalidRequest = SafeSanitisationRequest(modelRequest: SafeRequest() with { Messages = [] });
        var invalidResponse = SafeSanitisationRequest(response: SafeResponse(SafeRequest()) with { RequestId = "other" });
        var invalidAudit = SafeSanitisationRequest(audit: SafeAudit(SafeRequest()) with { AuditId = string.Empty });

        var requestResult = sanitiser.Sanitise(invalidRequest);
        var responseResult = sanitiser.Sanitise(invalidResponse);
        var auditResult = sanitiser.Sanitise(invalidAudit);

        Assert.AreEqual(AgentModelSanitisationStatus.Rejected, requestResult.Status);
        Assert.AreEqual(AgentModelSanitisationStatus.Rejected, responseResult.Status);
        Assert.AreEqual(AgentModelSanitisationStatus.Rejected, auditResult.Status);
        AssertHasIssue(requestResult, AgentModelAuditSanitiser.RequestRejectedByValidator);
        AssertHasIssue(responseResult, AgentModelAuditSanitiser.ResponseRejectedByValidator);
        AssertHasIssue(auditResult, AgentModelAuditSanitiser.AuditRejectedByValidator);
    }

    [TestMethod]
    public void AgentModelAuditSanitiser_RejectedResultExposesNoUnsafeRetentionObjects()
    {
        var result = new AgentModelAuditSanitiser().Sanitise(RequestWithMessage("private reasoning approval granted api_key=abc123secret"));

        Assert.AreEqual(AgentModelSanitisationStatus.Rejected, result.Status);
        Assert.IsNull(result.Prompt);
        Assert.IsNull(result.Response);
        Assert.IsNull(result.Audit);
        Assert.IsTrue(result.Redactions.Count > 0);
        Assert.IsTrue(result.Issues.Any(issue => string.Equals(issue.Severity, "error", StringComparison.Ordinal)));
        Assert.IsFalse(result.Redactions.Any(redaction => redaction.Reason.Contains("abc123secret", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void AgentModelAuditSanitiser_NoWiringIntoRuntimeStorageOrApi()
    {
        var forbiddenFiles = new[]
        {
            ReadRepositoryFile("IronDev.Core", "Agents", "Concrete", "ManualIndependentCriticAgentService.cs"),
            ReadRepositoryFile("IronDev.Core", "Agents", "Concrete", "ManualMemoryImprovementAgentService.cs"),
            ReadRepositoryFile("IronDev.Core", "Agents", "Concrete", "StoredManualAgentExecutionService.cs"),
            ReadRepositoryFile("IronDev.Api", "Program.cs")
        };

        foreach (var text in forbiddenFiles)
        {
            Assert.IsFalse(text.Contains(nameof(AgentModelAuditSanitiser), StringComparison.Ordinal));
            Assert.IsFalse(text.Contains(nameof(IAgentModelAuditSanitiser), StringComparison.Ordinal));
        }

        AssertNoRepositoryFileContains("IronDev.Api", nameof(AgentModelAuditSanitiser));
        AssertNoRepositoryFileContains("IronDev.Infrastructure", nameof(AgentModelAuditSanitiser));
        AssertNoRepositoryFileContains("tools", nameof(AgentModelAuditSanitiser));
    }

    [TestMethod]
    public void AgentModelAuditSanitiser_ProductionFileDoesNotAddRuntimeProviderPersistenceOrMutationCapabilities()
    {
        var source = ReadRepositoryFile("IronDev.Core", "Agents", "AgentModelAuditSanitisationModels.cs");
        var normalized = source
            .Replace(nameof(AgentModelRedactionKind.SourceMutationCommand), string.Empty, StringComparison.Ordinal)
            .Replace(nameof(AgentModelRedactionKind.GitHubReviewCommand), string.Empty, StringComparison.Ordinal)
            .Replace(nameof(AgentModelRedactionKind.MemoryPromotionCommand), string.Empty, StringComparison.Ordinal)
            .Replace(nameof(AgentModelSanitisedPrompt.ContainsSourceMutationCommand), string.Empty, StringComparison.Ordinal)
            .Replace(nameof(AgentModelSanitisedPrompt.ContainsMemoryPromotionCommand), string.Empty, StringComparison.Ordinal)
            .Replace(nameof(AgentModelSanitisedInvocationAudit.GrantsMemoryPromotion), string.Empty, StringComparison.Ordinal)
            .Replace("submit github review", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("delete file", string.Empty, StringComparison.OrdinalIgnoreCase);

        var forbiddenTokens = new[]
        {
            "OpenAiLlmService",
            "Anthropic",
            "Gemini",
            "Ollama",
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
            Assert.IsFalse(normalized.Contains(token, StringComparison.Ordinal), $"Sanitiser production file must not contain active token '{token}'.");
        }
    }

    private static AgentModelSanitisationRequest RequestWithMessage(string content) =>
        SafeSanitisationRequest(modelRequest: SafeRequest() with { Messages = [SafeMessage() with { Content = content }] });

    private static AgentModelSanitisationRequest RequestWithResponse(AgentModelResponse response) =>
        SafeSanitisationRequest(response: response);

    private static AgentModelSanitisationRequest RequestWithAudit(AgentModelInvocationAudit audit) =>
        SafeSanitisationRequest(audit: audit);

    private static AgentModelSanitisationRequest SafeSanitisationRequest(
        AgentModelRequest? modelRequest = null,
        AgentModelResponse? response = null,
        AgentModelInvocationAudit? audit = null,
        bool includeResponse = true,
        bool includeAudit = true,
        bool allowPreview = true,
        bool allowStructuredJson = true)
    {
        var request = modelRequest ?? SafeRequest();
        return new AgentModelSanitisationRequest
        {
            Request = request,
            Response = includeResponse ? response ?? SafeResponse(request) : null,
            Audit = includeAudit ? audit ?? SafeAudit(request) : null,
            AllowRedactedPreview = allowPreview,
            AllowStructuredJsonCandidate = allowStructuredJson
        };
    }

    private static AgentModelRequest SafeRequest() =>
        new()
        {
            RequestId = "model-request-001",
            TenantId = "tenant-1",
            ProjectId = "project-1",
            CampaignId = "campaign-1",
            AgentRunId = "agent-run-1",
            AgentId = AgentDefinitionCatalog.IndependentCriticAgent.AgentId,
            SpecialisationId = AgentSpecialisationCatalog.CodeReviewCritic.SpecialisationId,
            Profile = SafeProfile(),
            Messages = [SafeMessage()],
            Context = SafeContext(),
            ResponseFormat = SafeFormat(),
            CreatedAtUtc = Now
        };

    private static AgentModelProfile SafeProfile() =>
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
            AllowedAgentIds = [AgentDefinitionCatalog.IndependentCriticAgent.AgentId],
            AllowedSpecialisationIds = [AgentSpecialisationCatalog.CodeReviewCritic.SpecialisationId]
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
            MemoryRefs = ["memory-1"],
            AuditRefs = ["audit-1"]
        };

    private static AgentModelResponseFormat SafeFormat() =>
        new()
        {
            FormatId = "format-json-contract",
            OutputContractName = nameof(CriticReviewResult),
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

    private static void AssertRejectedWithoutPreviews(
        AgentModelSanitisationRequest request,
        AgentModelRedactionKind expectedRedaction)
    {
        var result = new AgentModelAuditSanitiser().Sanitise(request);

        Assert.AreEqual(AgentModelSanitisationStatus.Rejected, result.Status, expectedRedaction.ToString());
        Assert.IsNull(result.Prompt);
        Assert.IsNull(result.Response);
        Assert.IsNull(result.Audit);
        Assert.IsTrue(result.Issues.Any(issue => string.Equals(issue.Severity, "error", StringComparison.Ordinal)));
        Assert.IsTrue(
            result.Redactions.Any(redaction => redaction.Kind == expectedRedaction),
            $"Expected redaction kind {expectedRedaction}, got {string.Join(", ", result.Redactions.Select(redaction => redaction.Kind))}.");
    }

    private static void AssertHasIssue(AgentModelSanitisationResult result, string code) =>
        Assert.IsTrue(
            result.Issues.Any(issue => string.Equals(issue.Code, code, StringComparison.Ordinal)),
            $"Expected issue code '{code}' but got: {string.Join(", ", result.Issues.Select(issue => issue.Code))}");

    private static void AssertNoUnsafePromptFlags(AgentModelSanitisedPrompt prompt)
    {
        Assert.IsFalse(prompt.ContainsRawPrivateReasoning);
        Assert.IsFalse(prompt.ContainsPromptLeak);
        Assert.IsFalse(prompt.ContainsAuthorityClaim);
        Assert.IsFalse(prompt.ContainsToolCommand);
        Assert.IsFalse(prompt.ContainsSourceMutationCommand);
        Assert.IsFalse(prompt.ContainsMemoryPromotionCommand);
    }

    private static void AssertNoUnsafeResponseFlags(AgentModelSanitisedResponse response)
    {
        Assert.IsFalse(response.ContainsRawPrivateReasoning);
        Assert.IsFalse(response.ContainsPromptLeak);
        Assert.IsFalse(response.ContainsAuthorityClaim);
        Assert.IsFalse(response.ContainsToolCommand);
        Assert.IsFalse(response.ContainsSourceMutationCommand);
        Assert.IsFalse(response.ContainsMemoryPromotionCommand);
        Assert.IsFalse(response.GrantsAuthority);
        Assert.IsFalse(response.GrantsApproval);
        Assert.IsFalse(response.GrantsMemoryPromotion);
    }

    private static void AssertNoUnsafeAuditFlags(AgentModelSanitisedInvocationAudit audit)
    {
        Assert.IsFalse(audit.ContainsRawPrivateReasoning);
        Assert.IsFalse(audit.ContainsPromptLeak);
        Assert.IsFalse(audit.ContainsAuthorityClaim);
        Assert.IsFalse(audit.ContainsToolCommand);
        Assert.IsFalse(audit.ContainsSourceMutationCommand);
        Assert.IsFalse(audit.ContainsMemoryPromotionCommand);
        Assert.IsFalse(audit.GrantsAuthority);
        Assert.IsFalse(audit.GrantsApproval);
        Assert.IsFalse(audit.GrantsMemoryPromotion);
    }

    private static void AssertNoRepositoryFileContains(string directory, string token)
    {
        var root = Path.Combine(RepositoryRoot(), directory);
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            Assert.IsFalse(text.Contains(token, StringComparison.Ordinal), $"Did not expect {token} in {file}.");
        }
    }

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
