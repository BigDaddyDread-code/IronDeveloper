using IronDev.Core.Agents;
using IronDev.Core.Agents.Audit;
using IronDev.Core.Agents.Concrete;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Agents;

[TestClass]
public sealed class ModelBackedManualMemoryImprovementAgentTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 11, 14, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void ModelBackedManualMemoryImprovementAgent_ContractsExist()
    {
        Assert.AreEqual(nameof(IModelBackedManualMemoryImprovementAgentService), typeof(IModelBackedManualMemoryImprovementAgentService).Name);
        Assert.AreEqual(nameof(ModelBackedManualMemoryImprovementAgentService), typeof(ModelBackedManualMemoryImprovementAgentService).Name);
        Assert.AreEqual(nameof(ModelBackedMemoryImprovementDetectionRequest), typeof(ModelBackedMemoryImprovementDetectionRequest).Name);
        Assert.AreEqual(nameof(ModelBackedMemoryImprovementDetectionResult), typeof(ModelBackedMemoryImprovementDetectionResult).Name);
        Assert.AreEqual(nameof(ModelBackedMemoryImprovementIssue), typeof(ModelBackedMemoryImprovementIssue).Name);
        Assert.AreEqual(nameof(ModelBackedMemoryImprovementValidator), typeof(ModelBackedMemoryImprovementValidator).Name);
        Assert.AreEqual(nameof(ModelBackedMemoryImprovementPromptBuilder), typeof(ModelBackedMemoryImprovementPromptBuilder).Name);
        Assert.AreEqual(nameof(ModelBackedMemoryImprovementResponseParser), typeof(ModelBackedMemoryImprovementResponseParser).Name);
    }

    [TestMethod]
    public void ModelBackedManualMemoryImprovementAgent_SafeModelOutputProducesProposalOnlyResultAndAudit()
    {
        var result = CreateService(SafeMemoryResponseJson()).Detect(SafeRequest(), Now);

        Assert.IsTrue(result.Succeeded, FormatIssues(result));
        Assert.IsNotNull(result.DetectionResult);
        Assert.IsNotNull(result.AuditEnvelope);
        Assert.IsNotNull(result.ModelAdapterResult);
        Assert.IsNotNull(result.SanitisationResult);
        Assert.AreEqual(AgentModelSanitisationStatus.Safe, result.SanitisationResult.Status);

        Assert.AreEqual(AgentDefinitionCatalog.MemoryImprovementAgent.AgentId, result.DetectionResult.DetectedByAgentId);
        Assert.AreEqual("correlation-1", result.DetectionResult.CorrelationId);
        Assert.AreEqual(1, result.DetectionResult.Findings.Count);
        Assert.AreEqual(1, result.DetectionResult.ProposalDrafts.Count);
        Assert.IsTrue(result.DetectionResult.Findings.All(finding => finding.RequiresHumanReview));
        Assert.IsTrue(result.DetectionResult.ProposalDrafts.All(draft => draft.IsProposalOnly));
        Assert.IsTrue(result.DetectionResult.ProposalDrafts.All(draft => !draft.CreatesCollectiveMemory));
        Assert.IsTrue(result.DetectionResult.ProposalDrafts.All(draft => !draft.PromotesMemory));
        Assert.IsTrue(result.DetectionResult.ProposalDrafts.All(draft => draft.RequiresHumanReview));

        AssertNoValidationIssues(new MemoryImprovementDetectionResultValidator().Validate(result.DetectionResult));
        AssertNoValidationIssues(new AgentRunAuditEnvelopeValidator().Validate(result.AuditEnvelope));
        AssertNoValidationIssues(new ThoughtLedgerSafetyValidator().Validate(result.AuditEnvelope.ThoughtLedger));
        AssertAuditEnvelopeIsSafe(result.AuditEnvelope);
    }

    [TestMethod]
    public void ModelBackedManualMemoryImprovementAgent_AcceptsEveryMemoryImprovementProfile()
    {
        foreach (var profile in AgentSpecialisationCatalog.MemoryImprovementProfiles)
        {
            var request = SafeRequest(profile.SpecialisationId);
            var result = CreateService(SafeMemoryResponseJson()).Detect(request, Now);

            Assert.IsTrue(result.Succeeded, $"{profile.SpecialisationId}: {FormatIssues(result)}");
            Assert.IsNotNull(result.AuditEnvelope);
            Assert.AreEqual(profile.SpecialisationId, result.AuditEnvelope.Inputs.Single(input => input.RefType == nameof(AgentSpecialisationDefinition)).RefId);
        }
    }

    [TestMethod]
    public void ModelBackedManualMemoryImprovementAgent_RejectsCriticOrUnknownSpecialisationBeforeAdapter()
    {
        var criticAdapter = new CountingAgentModelAdapter(SafeMemoryResponseJson());
        var criticResult = new ModelBackedManualMemoryImprovementAgentService(criticAdapter)
            .Detect(SafeRequest(AgentSpecialisationCatalog.CodeReviewCritic.SpecialisationId), Now);

        Assert.IsFalse(criticResult.Succeeded);
        Assert.AreEqual(0, criticAdapter.InvocationCount);
        AssertHasIssue(criticResult, ModelBackedMemoryImprovementValidator.SpecialisationInvalid);

        var unknownAdapter = new CountingAgentModelAdapter(SafeMemoryResponseJson());
        var unknownResult = new ModelBackedManualMemoryImprovementAgentService(unknownAdapter)
            .Detect(SafeRequest("builtin.memory.unknown"), Now);

        Assert.IsFalse(unknownResult.Succeeded);
        Assert.AreEqual(0, unknownAdapter.InvocationCount);
        AssertHasIssue(unknownResult, ModelBackedMemoryImprovementValidator.SpecialisationInvalid);
    }

    [TestMethod]
    public void ModelBackedManualMemoryImprovementAgent_RejectsAuthorityGrantingSpecialisationBeforeAdapter()
    {
        var unsafeProfile = AgentSpecialisationCatalog.RepeatedGovernanceBlockDetector with
        {
            AuthorityBoundary = new AgentSpecialisationAuthorityBoundary { CanPromoteMemory = true }
        };
        var adapter = new CountingAgentModelAdapter(SafeMemoryResponseJson());
        var service = new ModelBackedManualMemoryImprovementAgentService(
            adapter,
            requestValidator: new ModelBackedMemoryImprovementValidator([unsafeProfile]));

        var result = service.Detect(SafeRequest(unsafeProfile.SpecialisationId), Now);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(0, adapter.InvocationCount);
        AssertHasIssue(result, ModelBackedMemoryImprovementValidator.SpecialisationInvalid);
        AssertHasIssue(result, ModelBackedMemoryImprovementValidator.SpecialisationAuthorityBlocked);
    }

    [TestMethod]
    public void ModelBackedManualMemoryImprovementAgent_RejectsUnsafeModelProfilesBeforeAdapter()
    {
        var cases = new[]
        {
            SafeRequest() with { ModelProfile = SafeProfile() with { ProviderKind = AgentModelProviderKind.OpenAI } },
            SafeRequest() with { ModelProfile = SafeProfile() with { ProviderKind = AgentModelProviderKind.Anthropic } },
            SafeRequest() with { ModelProfile = SafeProfile() with { ProviderKind = AgentModelProviderKind.Gemini } },
            SafeRequest() with { ModelProfile = SafeProfile() with { ProviderKind = AgentModelProviderKind.Ollama } },
            SafeRequest() with { ModelProfile = SafeProfile() with { ProviderKind = AgentModelProviderKind.LocalOpenAICompatible } },
            SafeRequest() with { ModelProfile = SafeProfile() with { AllowsToolCalls = true } },
            SafeRequest() with { ModelProfile = SafeProfile() with { AllowsExternalNetwork = true } },
            SafeRequest() with { ModelProfile = SafeProfile() with { AllowedAgentIds = [AgentDefinitionCatalog.IndependentCriticAgent.AgentId] } },
            SafeRequest() with { ModelProfile = SafeProfile() with { AllowedSpecialisationIds = [AgentSpecialisationCatalog.StaleMemoryDetector.SpecialisationId] } }
        };

        foreach (var request in cases)
        {
            var adapter = new CountingAgentModelAdapter(SafeMemoryResponseJson());
            var result = new ModelBackedManualMemoryImprovementAgentService(adapter).Detect(request, Now);

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(0, adapter.InvocationCount);
            AssertHasIssue(result, ModelBackedMemoryImprovementValidator.ModelProfileInvalid);
        }
    }

    [TestMethod]
    public void ModelBackedManualMemoryImprovementAgent_RejectsUnsafeInputsBeforeAdapter()
    {
        var cases = new[]
        {
            SafeRequest() with { Inputs = [] },
            SafeRequest() with { Inputs = [SafeInput() with { ContainsRawPrivateReasoning = true }] },
            SafeRequest() with { Inputs = [SafeInput() with { IsAuthoritativeForAction = true }] },
            SafeRequest() with { EvidenceRefs = [] },
            SafeRequest() with { ResponseFormat = SafeFormat() with { RequiresJson = false } }
        };

        foreach (var request in cases)
        {
            var adapter = new CountingAgentModelAdapter(SafeMemoryResponseJson());
            var result = new ModelBackedManualMemoryImprovementAgentService(adapter).Detect(request, Now);

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(0, adapter.InvocationCount);
        }
    }

    [TestMethod]
    public void ModelBackedManualMemoryImprovementAgent_SanitiserRejectionReturnsNoDetectionOrAudit()
    {
        var adapter = new UnsafeSucceededAdapter(responseContent: "approved for execution", structuredJson: SafeMemoryResponseJson());
        var result = new ModelBackedManualMemoryImprovementAgentService(adapter).Detect(SafeRequest(), Now);

        Assert.IsFalse(result.Succeeded);
        Assert.IsNull(result.DetectionResult);
        Assert.IsNull(result.AuditEnvelope);
        Assert.IsNotNull(result.ModelAdapterResult);
        Assert.IsNotNull(result.SanitisationResult);
        Assert.AreEqual(AgentModelSanitisationStatus.Rejected, result.SanitisationResult.Status);
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == AgentModelAuditSanitiser.ResponseRejectedByValidator ||
                                                issue.Code == AgentModelAuditSanitiser.UnsafeResponseMaterialRejected));
    }

    [TestMethod]
    public void ModelBackedManualMemoryImprovementAgent_SecretRedactionStillAllowsSafeStructuredOutput()
    {
        var adapter = new CountingAgentModelAdapter(SafeMemoryResponseJson(), responseContent: "Detection candidate includes api_key=abc123secret in evidence label.");
        var result = new ModelBackedManualMemoryImprovementAgentService(adapter).Detect(SafeRequest(), Now);

        Assert.IsTrue(result.Succeeded, FormatIssues(result));
        Assert.IsNotNull(result.SanitisationResult);
        Assert.AreEqual(AgentModelSanitisationStatus.SafeWithRedactions, result.SanitisationResult.Status);
        Assert.IsTrue(result.SanitisationResult.Redactions.Count > 0);
        Assert.IsNotNull(result.DetectionResult);
        Assert.IsNotNull(result.AuditEnvelope);
    }

    [TestMethod]
    public void ModelBackedManualMemoryImprovementAgent_ParserRejectsMalformedProposalOnlyOutput()
    {
        var cases = new[]
        {
            """{"summary":"No usable pattern.","patterns":[],"proposalDrafts":[]}""",
            """{"summary":"Pattern without evidence.","patterns":[{"patternType":"RepeatedGovernanceBlock","summary":"Repeated boundary miss.","confidence":0.7,"evidenceRefs":[],"requiresHumanReview":true}],"proposalDrafts":[]}""",
            """{"summary":"Bad confidence.","patterns":[{"patternType":"RepeatedGovernanceBlock","summary":"Repeated boundary miss.","confidence":2,"evidenceRefs":["evidence-1"],"requiresHumanReview":true}],"proposalDrafts":[]}""",
            """{"summary":"Proposal without evidence.","patterns":[{"patternType":"RepeatedGovernanceBlock","summary":"Repeated boundary miss.","confidence":0.7,"evidenceRefs":["evidence-1"],"requiresHumanReview":true}],"proposalDrafts":[{"title":"Record pattern","summary":"Pattern summary.","rationale":"Pattern rationale.","sourcePatternIndex":0,"evidenceRefs":[],"isProposalOnly":true,"createsCollectiveMemory":false,"promotesMemory":false,"requiresHumanReview":true}]}""",
            """{"summary":"Proposal with bad source.","patterns":[{"patternType":"RepeatedGovernanceBlock","summary":"Repeated boundary miss.","confidence":0.7,"evidenceRefs":["evidence-1"],"requiresHumanReview":true}],"proposalDrafts":[{"title":"Record pattern","summary":"Pattern summary.","rationale":"Pattern rationale.","sourcePatternIndex":5,"evidenceRefs":["evidence-1"],"isProposalOnly":true,"createsCollectiveMemory":false,"promotesMemory":false,"requiresHumanReview":true}]}"""
        };

        foreach (var json in cases)
        {
            var result = CreateService(json).Detect(SafeRequest(), Now);

            Assert.IsFalse(result.Succeeded, json);
            Assert.IsNull(result.DetectionResult);
            Assert.IsNull(result.AuditEnvelope);
        }
    }

    [TestMethod]
    public void ModelBackedManualMemoryImprovementAgent_NoProposalReasonIsAllowedWhenNoPatternExists()
    {
        var json = """{"summary":"Evidence was too weak.","noProposalReason":"PatternTooWeak","patterns":[],"proposalDrafts":[]}""";
        var result = CreateService(json).Detect(SafeRequest(), Now);

        Assert.IsTrue(result.Succeeded, FormatIssues(result));
        Assert.IsNotNull(result.DetectionResult);
        Assert.AreEqual(MemoryImprovementNoProposalReason.PatternTooWeak, result.DetectionResult.NoProposalReason);
        Assert.AreEqual(0, result.DetectionResult.Findings.Count);
        Assert.AreEqual(0, result.DetectionResult.ProposalDrafts.Count);
        Assert.IsNotNull(result.AuditEnvelope);
        Assert.AreEqual(IronDev.Core.Agents.Audit.AgentRunStatus.CompletedWithWarnings, result.AuditEnvelope.Run.Status);
    }

    [TestMethod]
    public void ModelBackedManualMemoryImprovementAgent_DoesNotWireIntoRuntimeApiOrStoredManualExecution()
    {
        var files = new[]
        {
            ReadRepositoryFile("IronDev.Core", "Agents", "Concrete", "ManualMemoryImprovementAgentService.cs"),
            ReadRepositoryFile("IronDev.Core", "Agents", "Concrete", "StoredManualAgentExecutionService.cs"),
            ReadRepositoryFile("IronDev.Core", "Agents", "Concrete", "ManualIndependentCriticAgentService.cs"),
            ReadRepositoryFile("IronDev.Api", "Program.cs")
        };

        foreach (var file in files)
        {
            Assert.IsFalse(file.Contains(nameof(IModelBackedManualMemoryImprovementAgentService), StringComparison.Ordinal));
            Assert.IsFalse(file.Contains(nameof(ModelBackedManualMemoryImprovementAgentService), StringComparison.Ordinal));
        }
    }

    [TestMethod]
    public void ModelBackedManualMemoryImprovementAgent_ProductionFileDoesNotAddRuntimePersistenceOrExternalCapabilities()
    {
        var source = ReadRepositoryFile("IronDev.Core", "Agents", "Concrete", "ModelBackedManualMemoryImprovementAgentService.cs");
        var normalized = source
            .Replace(nameof(IAgentModelAdapter), string.Empty, StringComparison.Ordinal)
            .Replace(nameof(IAgentModelAuditSanitiser), string.Empty, StringComparison.Ordinal)
            .Replace(nameof(AgentModelAdapterValidator), string.Empty, StringComparison.Ordinal)
            .Replace(nameof(AgentModelAuditSanitiser), string.Empty, StringComparison.Ordinal)
            .Replace("PromoteCollectiveMemory", string.Empty, StringComparison.Ordinal)
            .Replace("CollectiveMemory", string.Empty, StringComparison.Ordinal)
            .Replace("promote memory", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("accepted memory", string.Empty, StringComparison.OrdinalIgnoreCase);

        var forbiddenTokens = new[]
        {
            "HttpClient",
            "OpenAiLlmService",
            "ChatCompletion",
            "ResponsesApi",
            "SqlConnection",
            "INSERT INTO",
            "UPDATE ",
            "DELETE ",
            "File.WriteAllText",
            "File.Copy",
            "File.Delete",
            "ProcessStartInfo",
            "AgentRuntime",
            "AgentScheduler",
            "AgentOrchestrator",
            "AgentToolRouter",
            "SubmitReview",
            "GitHub",
            "WeaviateClient",
            "SqlMemoryImprovementProposalStore",
            "SqlCollectiveMemoryPromotionService",
            "IAgentRunAuditEnvelopeStore"
        };

        foreach (var token in forbiddenTokens)
        {
            Assert.IsFalse(normalized.Contains(token, StringComparison.Ordinal), $"Model-backed memory-improvement service must not contain active token '{token}'.");
        }
    }

    private static ModelBackedManualMemoryImprovementAgentService CreateService(string structuredJson) =>
        new(new CountingAgentModelAdapter(structuredJson));

    private static ModelBackedMemoryImprovementDetectionRequest SafeRequest(string? specialisationId = null)
    {
        specialisationId ??= AgentSpecialisationCatalog.RepeatedGovernanceBlockDetector.SpecialisationId;
        return new ModelBackedMemoryImprovementDetectionRequest
        {
            DetectionRequestId = "model-memory-detection-001",
            TenantId = "tenant-1",
            ProjectId = "project-1",
            CampaignId = "campaign-1",
            RunId = "run-1",
            RequestedByUserId = "user-1",
            CorrelationId = "correlation-1",
            SpecialisationId = specialisationId,
            ModelProfile = SafeProfile(specialisationId),
            RequestSummary = "Detect repeated governance-boundary mistakes from supplied evidence.",
            Inputs = [SafeInput()],
            EvidenceRefs = ["evidence-1"],
            ResponseFormat = SafeFormat()
        };
    }

    private static ManualMemoryImprovementInputRef SafeInput() =>
        new()
        {
            InputRefId = "input-1",
            RefType = "AgentRunAuditEnvelope",
            RefId = "audit-1",
            Source = "test",
            Summary = "Repeated governed runs reported the same missing review boundary.",
            EvidenceRefs = ["evidence-1"],
            ContainsRawPrivateReasoning = false,
            IsAuthoritativeForAction = false
        };

    private static AgentModelProfile SafeProfile(string? specialisationId = null) =>
        new()
        {
            ProfileId = "fake-memory-model-profile",
            DisplayName = "Fake Memory Model Profile",
            ProviderKind = AgentModelProviderKind.Fake,
            ModelName = "fake-memory-model",
            IsEnabled = true,
            AllowsToolCalls = false,
            AllowsJsonOutput = true,
            AllowsStreaming = false,
            AllowsExternalNetwork = false,
            MaxInputTokens = 4096,
            MaxOutputTokens = 1024,
            Temperature = 0.1m,
            AllowedAgentIds = [AgentDefinitionCatalog.MemoryImprovementAgent.AgentId],
            AllowedSpecialisationIds = [specialisationId ?? AgentSpecialisationCatalog.RepeatedGovernanceBlockDetector.SpecialisationId]
        };

    private static AgentModelResponseFormat SafeFormat() =>
        new()
        {
            FormatId = "memory-improvement-detection-json",
            OutputContractName = nameof(MemoryImprovementDetectionResult),
            RequiresJson = true,
            RequiresSchemaValidation = true,
            AllowsFreeText = false,
            RequiredFields = ["summary", "patterns", "proposalDrafts"]
        };

    private static string SafeMemoryResponseJson() =>
        """
        {
          "summary": "Repeated governance block pattern detected.",
          "noProposalReason": "None",
          "patterns": [
            {
              "patternType": "RepeatedGovernanceBlock",
              "summary": "Multiple runs failed for the same missing review boundary.",
              "confidence": 0.74,
              "evidenceRefs": ["evidence-1"],
              "requiresHumanReview": true
            }
          ],
          "proposalDrafts": [
            {
              "title": "Record repeated missing review boundary",
              "summary": "Future runs should be warned that this action requires explicit human review.",
              "rationale": "The pattern repeated across governed runs.",
              "sourcePatternIndex": 0,
              "evidenceRefs": ["evidence-1"],
              "isProposalOnly": true,
              "createsCollectiveMemory": false,
              "promotesMemory": false,
              "requiresHumanReview": true
            }
          ]
        }
        """;

    private static void AssertAuditEnvelopeIsSafe(AgentRunAuditEnvelope envelope)
    {
        Assert.AreEqual(AgentDefinitionCatalog.MemoryImprovementAgent.AgentId, envelope.Run.AgentId);
        Assert.AreEqual(AgentRunTriggerType.ManualUserRequest, envelope.Run.TriggerType);
        Assert.IsTrue(envelope.Outputs.Any(output => output.RefType == nameof(MemoryImprovementDetectionResult) && output.IsProposalOnly));
        Assert.IsTrue(envelope.Outputs.Where(output => output.RefType == nameof(MemoryImprovementProposalDraft)).All(output => output.IsProposalOnly));
        Assert.IsTrue(envelope.Outputs.All(output => !output.CreatesAuthority));
        Assert.IsTrue(envelope.Outputs.All(output => !output.CreatesRuntimeAction));
        Assert.IsTrue(envelope.Outputs.All(output => !output.ContainsRawPrivateReasoning));
        Assert.IsTrue(envelope.Inputs.All(input => !input.IsAuthoritativeForAction));
        Assert.IsTrue(envelope.CapabilityUses.Any(use => use.Capability == AgentCapability.CreateMemoryProposal && use.Outcome == AgentCapabilityUseOutcome.Allowed));
        Assert.IsTrue(envelope.CapabilityUses.Any(use => use.Capability == AgentCapability.CreateReport && use.Outcome == AgentCapabilityUseOutcome.Allowed));
        Assert.IsTrue(envelope.CapabilityUses.Any(use => use.Capability == AgentCapability.PromoteCollectiveMemory && use.Outcome == AgentCapabilityUseOutcome.Blocked));
        Assert.IsTrue(envelope.CapabilityUses.Any(use => use.Capability == AgentCapability.RunTool && use.Outcome == AgentCapabilityUseOutcome.Blocked));
        Assert.IsTrue(envelope.CapabilityUses.Any(use => use.Capability == AgentCapability.MutateSource && use.Outcome == AgentCapabilityUseOutcome.Blocked));
        Assert.IsTrue(envelope.CapabilityUses.Any(use => use.Capability == AgentCapability.CallExternalSystem && use.Outcome == AgentCapabilityUseOutcome.Blocked));
        Assert.IsTrue(envelope.BoundaryDecisions.All(decision => !decision.GrantsAuthority));
        Assert.IsTrue(envelope.BoundaryDecisions.All(decision => !decision.GrantsHumanApproval));
        Assert.IsTrue(envelope.BoundaryDecisions.All(decision => !decision.GrantsPolicyApproval));
        Assert.IsTrue(envelope.BoundaryDecisions.All(decision => !decision.GrantsMemoryPromotion));
        Assert.IsTrue(envelope.ThoughtLedger.All(entry => !entry.GrantsAuthority));
        Assert.IsTrue(envelope.ThoughtLedger.All(entry => !entry.GrantsApproval));
        Assert.IsTrue(envelope.ThoughtLedger.All(entry => !entry.GrantsMemoryPromotion));
        Assert.IsTrue(envelope.ThoughtLedger.All(entry => !entry.ContainsRawPrivateReasoning));
    }

    private static void AssertNoValidationIssues(IReadOnlyList<AgentDefinitionValidationIssue> issues)
    {
        var errors = issues.Where(issue => string.Equals(issue.Severity, AgentDefinitionValidator.SeverityError, StringComparison.OrdinalIgnoreCase)).ToArray();
        Assert.AreEqual(0, errors.Length, string.Join(Environment.NewLine, errors.Select(issue => $"{issue.Code}: {issue.Message}")));
    }

    private static void AssertHasIssue(ModelBackedMemoryImprovementDetectionResult result, string code) =>
        Assert.IsTrue(result.Issues.Any(issue => string.Equals(issue.Code, code, StringComparison.Ordinal)), $"Expected issue '{code}'. Actual: {string.Join(", ", result.Issues.Select(issue => issue.Code))}");

    private static string FormatIssues(ModelBackedMemoryImprovementDetectionResult result) =>
        string.Join(Environment.NewLine, result.Issues.Select(issue => $"{issue.Code}: {issue.Message}"));

    private sealed class CountingAgentModelAdapter : IAgentModelAdapter
    {
        private readonly string _structuredJson;
        private readonly string _responseContent;

        public CountingAgentModelAdapter(string structuredJson, string responseContent = "Safe memory-improvement detection candidate.")
        {
            _structuredJson = structuredJson;
            _responseContent = responseContent;
        }

        public int InvocationCount { get; private set; }

        public AgentModelAdapterResult Invoke(AgentModelRequest request, DateTimeOffset invokedAtUtc)
        {
            InvocationCount++;
            var response = SafeResponse(request, invokedAtUtc, _structuredJson, _responseContent);
            var audit = SafeAudit(request, invokedAtUtc);
            var issues = new AgentModelAdapterValidator().ValidateResponse(request, response)
                .Concat(new AgentModelAdapterValidator().ValidateAudit(audit))
                .ToArray();

            return new AgentModelAdapterResult
            {
                Succeeded = issues.Length == 0,
                Response = response,
                Audit = audit,
                Issues = issues
            };
        }
    }

    private sealed class UnsafeSucceededAdapter : IAgentModelAdapter
    {
        private readonly string _responseContent;
        private readonly string _structuredJson;

        public UnsafeSucceededAdapter(string responseContent, string structuredJson)
        {
            _responseContent = responseContent;
            _structuredJson = structuredJson;
        }

        public AgentModelAdapterResult Invoke(AgentModelRequest request, DateTimeOffset invokedAtUtc) =>
            new()
            {
                Succeeded = true,
                Response = SafeResponse(request, invokedAtUtc, _structuredJson, _responseContent) with
                {
                    ContainsAuthorityClaim = true
                },
                Audit = SafeAudit(request, invokedAtUtc),
                Issues = []
            };
    }

    private static AgentModelResponse SafeResponse(
        AgentModelRequest request,
        DateTimeOffset completedAtUtc,
        string structuredJson,
        string content) =>
        new()
        {
            ResponseId = $"response-{request.RequestId}",
            RequestId = request.RequestId,
            AgentId = request.AgentId,
            SpecialisationId = request.SpecialisationId,
            ProviderKind = AgentModelProviderKind.Fake,
            ModelName = request.Profile.ModelName,
            Content = content,
            StructuredJson = structuredJson,
            Usage = new AgentModelUsage { InputTokens = 22, OutputTokens = 31 },
            CompletedAtUtc = completedAtUtc
        };

    private static AgentModelInvocationAudit SafeAudit(AgentModelRequest request, DateTimeOffset completedAtUtc) =>
        new()
        {
            AuditId = $"audit-{request.RequestId}",
            RequestId = request.RequestId,
            AgentId = request.AgentId,
            SpecialisationId = request.SpecialisationId,
            ProfileId = request.Profile.ProfileId,
            ProviderKind = AgentModelProviderKind.Fake,
            ModelName = request.Profile.ModelName,
            RequestedAtUtc = request.CreatedAtUtc,
            CompletedAtUtc = completedAtUtc,
            Succeeded = true,
            InputRefs = request.Context.InputRefs,
            EvidenceRefs = request.Context.EvidenceRefs,
            Usage = new AgentModelUsage { InputTokens = 22, OutputTokens = 31 }
        };

    private static string ReadRepositoryFile(params string[] segments) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), Path.Combine(segments)));

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
