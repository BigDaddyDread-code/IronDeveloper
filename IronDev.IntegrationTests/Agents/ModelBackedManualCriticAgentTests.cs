using System.Text.Json;
using IronDev.Core.Agents;
using IronDev.Core.Agents.Audit;
using IronDev.Core.Agents.Concrete;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Agents;

[TestClass]
public sealed class ModelBackedManualCriticAgentTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 11, 18, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void ModelBackedManualCriticAgent_ContractsExist()
    {
        Assert.AreEqual(nameof(ModelBackedCriticReviewRequest), typeof(ModelBackedCriticReviewRequest).Name);
        Assert.AreEqual(nameof(ModelBackedCriticReviewResult), typeof(ModelBackedCriticReviewResult).Name);
        Assert.AreEqual(nameof(ModelBackedCriticReviewIssue), typeof(ModelBackedCriticReviewIssue).Name);
        Assert.AreEqual(nameof(IModelBackedManualIndependentCriticAgentService), typeof(IModelBackedManualIndependentCriticAgentService).Name);
        Assert.AreEqual(nameof(ModelBackedManualIndependentCriticAgentService), typeof(ModelBackedManualIndependentCriticAgentService).Name);
        Assert.AreEqual(nameof(ModelBackedCriticReviewValidator), typeof(ModelBackedCriticReviewValidator).Name);
        Assert.AreEqual(nameof(ModelBackedCriticPromptBuilder), typeof(ModelBackedCriticPromptBuilder).Name);
        Assert.AreEqual(nameof(ModelBackedCriticResponseParser), typeof(ModelBackedCriticResponseParser).Name);
    }

    [TestMethod]
    public void ModelBackedManualCriticAgent_ValidScriptedModelProducesReviewOnlyResultAndSafeAudit()
    {
        var result = BuildService(ScriptedAdapter(SafeCriticJson())).Review(BuildRequest(), Now);

        Assert.IsTrue(result.Succeeded, FormatIssues(result.Issues));
        Assert.IsNotNull(result.CriticReviewResult);
        Assert.IsNotNull(result.AuditEnvelope);
        Assert.IsNotNull(result.ModelAdapterResult);
        Assert.IsNotNull(result.SanitisationResult);
        Assert.AreEqual(AgentModelSanitisationStatus.Safe, result.SanitisationResult.Status);
        Assert.AreEqual("critic-review-model-review-001", result.CriticReviewResult.ReviewResultId);
        Assert.AreEqual(CriticReviewVerdict.RequestChanges, result.CriticReviewResult.Verdict);
        Assert.AreEqual(AgentDefinitionCatalog.IndependentCriticAgent.AgentId, result.CriticReviewResult.ReviewedByAgentId);
        Assert.IsTrue(result.CriticReviewResult.Findings.Single().RequiresHumanReview);
        AssertNoIssues(new CriticReviewResultValidator().Validate(result.CriticReviewResult));
        AssertNoIssues(new AgentRunAuditEnvelopeValidator().Validate(result.AuditEnvelope));
        AssertNoIssues(new ThoughtLedgerSafetyValidator().Validate(result.AuditEnvelope.ThoughtLedger));
        AssertAuditIsReviewOnly(result.AuditEnvelope);
        AssertModelAdapterAndSanitiserGrantedNoAuthority(result);
    }

    [TestMethod]
    public void ModelBackedManualCriticAgent_AllCriticProfilesCanUseTheManualModelBoundary()
    {
        foreach (var profile in AgentSpecialisationCatalog.CriticProfiles)
        {
            var result = BuildService(ScriptedAdapter(SafeCriticJson())).Review(
                BuildRequest(
                    reviewRequestId: $"model-review-{profile.SpecialisationId.Replace('.', '-')}",
                    specialisationId: profile.SpecialisationId),
                Now);

            Assert.IsTrue(result.Succeeded, $"{profile.SpecialisationId}: {FormatIssues(result.Issues)}");
            Assert.AreEqual(profile.SpecialisationId, result.AuditEnvelope!.Inputs.Single(input => input.RefType == "AgentSpecialisationDefinition").RefId);
        }
    }

    [TestMethod]
    public void ModelBackedManualCriticAgent_RejectsNonCriticSpecialisationBeforeAdapterCall()
    {
        var adapter = new CountingAdapter((request, invokedAtUtc) => SafeAdapterResult(request, invokedAtUtc, SafeCriticJson()));
        var result = BuildService(adapter).Review(
            BuildRequest(specialisationId: AgentSpecialisationCatalog.RepeatedFailureModeDetector.SpecialisationId),
            Now);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(0, adapter.CallCount);
        AssertHasIssue(result.Issues, ModelBackedCriticReviewValidator.SpecialisationInvalid);
        Assert.IsNull(result.CriticReviewResult);
        Assert.IsNull(result.AuditEnvelope);
    }

    [TestMethod]
    public void ModelBackedManualCriticAgent_RejectsAuthorityGrantingCriticSpecialisationBeforeAdapterCall()
    {
        var unsafeProfile = AgentSpecialisationCatalog.CodeReviewCritic with
        {
            AuthorityBoundary = new AgentSpecialisationAuthorityBoundary { CanGrantApproval = true }
        };
        var adapter = new CountingAdapter((request, invokedAtUtc) => SafeAdapterResult(request, invokedAtUtc, SafeCriticJson()));
        var service = BuildService(
            adapter,
            requestValidator: new ModelBackedCriticReviewValidator(specialisations: [unsafeProfile]));

        var result = service.Review(BuildRequest(), Now);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(0, adapter.CallCount);
        AssertHasIssue(result.Issues, ModelBackedCriticReviewValidator.SpecialisationInvalid);
    }

    [TestMethod]
    public void ModelBackedManualCriticAgent_RejectsRealProviderProfileBeforeAdapterCall()
    {
        var adapter = new CountingAdapter((request, invokedAtUtc) => SafeAdapterResult(request, invokedAtUtc, SafeCriticJson()));
        var result = BuildService(adapter).Review(
            BuildRequest(modelProfile: SafeProfile() with { ProviderKind = AgentModelProviderKind.OpenAI, ModelName = "gpt-test" }),
            Now);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(0, adapter.CallCount);
        AssertHasIssue(result.Issues, ModelBackedCriticReviewValidator.ProfileInvalid);
    }

    [TestMethod]
    public void ModelBackedManualCriticAgent_RejectsProfileThatAllowsToolsBeforeAdapterCall()
    {
        var adapter = new CountingAdapter((request, invokedAtUtc) => SafeAdapterResult(request, invokedAtUtc, SafeCriticJson()));
        var result = BuildService(adapter).Review(
            BuildRequest(modelProfile: SafeProfile() with { AllowsToolCalls = true }),
            Now);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(0, adapter.CallCount);
        AssertHasIssue(result.Issues, ModelBackedCriticReviewValidator.ProfileInvalid);
    }

    [TestMethod]
    public void ModelBackedManualCriticAgent_SanitiserRejectsUnsafeModelMaterialWithoutAuditEnvelope()
    {
        var result = BuildService(new UnsafeSucceededAdapter()).Review(BuildRequest(), Now);

        Assert.IsFalse(result.Succeeded);
        Assert.IsNotNull(result.ModelAdapterResult);
        Assert.IsNotNull(result.SanitisationResult);
        Assert.AreEqual(AgentModelSanitisationStatus.Rejected, result.SanitisationResult.Status);
        Assert.IsNull(result.CriticReviewResult);
        Assert.IsNull(result.AuditEnvelope);
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == AgentModelAuditSanitiser.UnsafeResponseMaterialRejected), FormatIssues(result.Issues));
    }

    [TestMethod]
    public void ModelBackedManualCriticAgent_RejectsStructuredOutputWithoutEvidenceRefs()
    {
        var json = SafeCriticJson(evidenceRefs: []);
        var result = BuildService(ScriptedAdapter(json)).Review(BuildRequest(), Now);

        Assert.IsFalse(result.Succeeded);
        Assert.IsNotNull(result.ModelAdapterResult);
        Assert.IsNotNull(result.SanitisationResult);
        Assert.IsNull(result.CriticReviewResult);
        Assert.IsNull(result.AuditEnvelope);
        AssertHasIssue(result.Issues, ModelBackedCriticResponseParser.FindingEvidenceRequired);
    }

    [TestMethod]
    public void ModelBackedManualCriticAgent_RejectsUnsafeStructuredOutputAtSanitisationBoundary()
    {
        var json = SafeCriticJson(requiredFix: "apply this patch");
        var result = BuildService(ScriptedAdapter(json)).Review(BuildRequest(), Now);

        Assert.IsFalse(result.Succeeded);
        Assert.IsNull(result.AuditEnvelope);
        AssertHasIssue(result.Issues, AgentModelAuditSanitiser.UnsafeResponseMaterialRejected);
    }

    [TestMethod]
    public void ModelBackedManualCriticAgent_AuditEnvelopeDoesNotRetainRawPromptOrRawCompletion()
    {
        var result = BuildService(ScriptedAdapter(SafeCriticJson())).Review(BuildRequest(), Now);
        var envelope = result.AuditEnvelope!;
        var serialized = JsonSerializer.Serialize(envelope);

        Assert.IsTrue(result.Succeeded, FormatIssues(result.Issues));
        Assert.IsFalse(serialized.Contains("raw prompt", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(serialized.Contains("raw completion", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(serialized.Contains("chain-of-thought", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(serialized.Contains("scratchpad", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(serialized.Contains("private reasoning", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(envelope.Inputs.Any(input => input.ContainsRawPrivateReasoning || input.IsAuthoritativeForAction));
        Assert.IsFalse(envelope.Outputs.Any(output => output.ContainsRawPrivateReasoning || output.CreatesAuthority || output.CreatesRuntimeAction));
        Assert.IsFalse(envelope.BoundaryDecisions.Any(decision => decision.GrantsAuthority || decision.GrantsHumanApproval || decision.GrantsPolicyApproval || decision.GrantsMemoryPromotion));
    }

    [TestMethod]
    public void ModelBackedManualCriticAgent_StaticBoundaryDoesNotWireRuntimePersistenceOrExternalCapabilities()
    {
        var source = ReadRepositoryFile("IronDev.Core", "Agents", "Concrete", "ModelBackedManualIndependentCriticAgentService.cs");
        var normalized = source
            .Replace("\"write file\"", string.Empty, StringComparison.Ordinal)
            .Replace("\"delete file\"", string.Empty, StringComparison.Ordinal)
            .Replace("\"modify source\"", string.Empty, StringComparison.Ordinal)
            .Replace("\"mutate source\"", string.Empty, StringComparison.Ordinal)
            .Replace("\"promote memory\"", string.Empty, StringComparison.Ordinal)
            .Replace("\"create CollectiveMemory\"", string.Empty, StringComparison.Ordinal)
            .Replace("\"persist proposal\"", string.Empty, StringComparison.Ordinal);

        foreach (var token in new[]
        {
            "HttpClient",
            "SqlConnection",
            "DbConnection",
            "ProcessStartInfo",
            "File.WriteAllText",
            "File.Copy",
            "File.Delete",
            "WeaviateClient",
            "AddHostedService",
            "IHostedService",
            "BackgroundService",
            "AgentScheduler",
            "AgentOrchestrator",
            "AgentToolRouter",
            "SubmitReview",
            "CreatePullRequest"
        })
        {
            Assert.IsFalse(normalized.Contains(token, StringComparison.Ordinal), token);
        }

        foreach (var productionFile in new[]
        {
            ReadRepositoryFile("IronDev.Core", "Agents", "Concrete", "ManualIndependentCriticAgentService.cs"),
            ReadRepositoryFile("IronDev.Core", "Agents", "Concrete", "StoredManualAgentExecutionService.cs"),
            ReadRepositoryFile("IronDev.Api", "Program.cs")
        })
        {
            Assert.IsFalse(productionFile.Contains(nameof(ModelBackedManualIndependentCriticAgentService), StringComparison.Ordinal));
            Assert.IsFalse(productionFile.Contains(nameof(IModelBackedManualIndependentCriticAgentService), StringComparison.Ordinal));
        }
    }

    private static ModelBackedManualIndependentCriticAgentService BuildService(
        IAgentModelAdapter adapter,
        ModelBackedCriticReviewValidator? requestValidator = null) =>
        new(
            adapter,
            new AgentModelAuditSanitiser(),
            requestValidator: requestValidator);

    private static ScriptedAgentModelAdapter ScriptedAdapter(string json) =>
        new((request, invokedAtUtc) => SafeResponse(request, invokedAtUtc, json));

    private static ModelBackedCriticReviewRequest BuildRequest(
        string reviewRequestId = "model-review-001",
        string specialisationId = "builtin.critic.code-review",
        AgentModelProfile? modelProfile = null) =>
        new()
        {
            ReviewRequestId = reviewRequestId,
            TenantId = "tenant-1",
            ProjectId = "project-1",
            CampaignId = "campaign-1",
            RunId = "run-1",
            RequestedByUserId = "user-1",
            CorrelationId = "corr-1",
            SubjectType = CriticReviewSubjectType.PullRequest,
            SubjectId = "pr-35",
            SpecialisationId = specialisationId,
            ModelProfile = modelProfile ?? SafeProfile(specialisationId: specialisationId),
            RequestSummary = "Review the supplied implementation evidence and identify any blockers.",
            Inputs =
            [
                new ManualCriticReviewInputRef
                {
                    InputRefId = "input-1",
                    RefType = "PullRequestDiff",
                    RefId = "diff-1",
                    Source = "test-fixture",
                    Summary = "The implementation adds a model-backed manual critic boundary.",
                    EvidenceRefs = ["evidence-1"]
                }
            ],
            EvidenceRefs = ["evidence-1"],
            ResponseFormat = SafeFormat()
        };

    private static AgentModelProfile SafeProfile(
        string agentId = "builtin.independent-critic",
        string specialisationId = "builtin.critic.code-review") =>
        new()
        {
            ProfileId = "fake-model-profile-critic",
            DisplayName = "Fake critic model profile",
            ProviderKind = AgentModelProviderKind.Fake,
            ModelName = "fake-critic-model",
            IsEnabled = true,
            AllowsToolCalls = false,
            AllowsJsonOutput = true,
            AllowsStreaming = false,
            AllowsExternalNetwork = false,
            MaxInputTokens = 4096,
            MaxOutputTokens = 1024,
            Temperature = 0,
            AllowedAgentIds = [agentId],
            AllowedSpecialisationIds = [specialisationId]
        };

    private static AgentModelResponseFormat SafeFormat() =>
        new()
        {
            FormatId = "critic-review-result-json",
            OutputContractName = nameof(CriticReviewResult),
            RequiresJson = true,
            RequiresSchemaValidation = true,
            AllowsFreeText = false,
            RequiredFields = ["summary", "verdict", "findings"]
        };

    private static string SafeCriticJson(
        IReadOnlyList<string>? evidenceRefs = null,
        string requiredFix = "Add a focused boundary regression test.") =>
        JsonSerializer.Serialize(new
        {
            summary = "Review found missing evidence.",
            verdict = "RequestChanges",
            findings = new[]
            {
                new
                {
                    severity = "High",
                    title = "Missing boundary evidence",
                    problem = "The change does not yet prove the model critic boundary.",
                    whyItMatters = "A regression could weaken review-only behaviour.",
                    requiredFix,
                    evidenceRefs = evidenceRefs ?? ["evidence-1"],
                    blocksMerge = true
                }
            }
        });

    private static AgentModelResponse SafeResponse(
        AgentModelRequest request,
        DateTimeOffset completedAtUtc,
        string json) =>
        new()
        {
            ResponseId = $"model-response-{request.RequestId}",
            RequestId = request.RequestId,
            AgentId = request.AgentId,
            SpecialisationId = request.SpecialisationId,
            ProviderKind = request.Profile.ProviderKind,
            ModelName = request.Profile.ModelName,
            Content = "Advisory review candidate for human review.",
            StructuredJson = json,
            Usage = new AgentModelUsage
            {
                InputTokens = 12,
                OutputTokens = 18
            },
            CompletedAtUtc = completedAtUtc
        };

    private static AgentModelAdapterResult SafeAdapterResult(
        AgentModelRequest request,
        DateTimeOffset invokedAtUtc,
        string json)
    {
        var response = SafeResponse(request, invokedAtUtc, json);
        return new AgentModelAdapterResult
        {
            Succeeded = true,
            Response = response,
            Audit = SafeAudit(request, response, invokedAtUtc)
        };
    }

    private static AgentModelInvocationAudit SafeAudit(
        AgentModelRequest request,
        AgentModelResponse response,
        DateTimeOffset invokedAtUtc) =>
        new()
        {
            AuditId = $"model-audit-{request.RequestId}",
            RequestId = request.RequestId,
            AgentId = request.AgentId,
            SpecialisationId = request.SpecialisationId,
            ProfileId = request.Profile.ProfileId,
            ProviderKind = request.Profile.ProviderKind,
            ModelName = request.Profile.ModelName,
            RequestedAtUtc = invokedAtUtc,
            CompletedAtUtc = response.CompletedAtUtc,
            Succeeded = true,
            InputRefs = request.Context.InputRefs,
            EvidenceRefs = request.Context.EvidenceRefs,
            Usage = response.Usage
        };

    private static void AssertAuditIsReviewOnly(AgentRunAuditEnvelope envelope)
    {
        var output = envelope.Outputs.Single();

        Assert.AreEqual(nameof(CriticReviewResult), output.RefType);
        Assert.IsTrue(output.IsReviewOnly);
        Assert.IsFalse(output.IsProposalOnly);
        Assert.IsFalse(output.CreatesAuthority);
        Assert.IsFalse(output.CreatesRuntimeAction);
        Assert.IsFalse(output.ContainsRawPrivateReasoning);
        Assert.AreEqual(AgentRunTriggerType.ManualUserRequest, envelope.Run.TriggerType);
        Assert.AreEqual(IronDev.Core.Agents.Audit.AgentRunStatus.CompletedWithWarnings, envelope.Run.Status);
        AssertCapability(envelope.CapabilityUses, AgentCapability.CreateCriticFinding, AgentCapabilityUseOutcome.Allowed);
        AssertCapability(envelope.CapabilityUses, AgentCapability.CreateReport, AgentCapabilityUseOutcome.Allowed);
        AssertCapability(envelope.CapabilityUses, AgentCapability.WarnExecution, AgentCapabilityUseOutcome.Allowed);
        AssertCapability(envelope.CapabilityUses, AgentCapability.BlockExecution, AgentCapabilityUseOutcome.Blocked);
        AssertCapability(envelope.CapabilityUses, AgentCapability.RunTool, AgentCapabilityUseOutcome.Blocked);
        AssertCapability(envelope.CapabilityUses, AgentCapability.MutateSource, AgentCapabilityUseOutcome.Blocked);
        AssertCapability(envelope.CapabilityUses, AgentCapability.CallExternalSystem, AgentCapabilityUseOutcome.Blocked);
        AssertCapability(envelope.CapabilityUses, AgentCapability.PromoteCollectiveMemory, AgentCapabilityUseOutcome.Blocked);
        AssertCapability(envelope.CapabilityUses, AgentCapability.RepresentHumanApproval, AgentCapabilityUseOutcome.Blocked);
        AssertCapability(envelope.CapabilityUses, AgentCapability.RepresentHumanPromotionDecision, AgentCapabilityUseOutcome.Blocked);
        Assert.IsTrue(envelope.BoundaryDecisions.All(decision =>
            !decision.GrantsAuthority &&
            !decision.GrantsHumanApproval &&
            !decision.GrantsPolicyApproval &&
            !decision.GrantsMemoryPromotion));
    }

    private static void AssertModelAdapterAndSanitiserGrantedNoAuthority(ModelBackedCriticReviewResult result)
    {
        Assert.IsNotNull(result.ModelAdapterResult?.Response);
        Assert.IsNotNull(result.ModelAdapterResult.Audit);
        Assert.IsFalse(result.ModelAdapterResult.Response.ClaimedSafetyFlags.MayGrantApproval);
        Assert.IsFalse(result.ModelAdapterResult.Response.ClaimedSafetyFlags.MayRunTools);
        Assert.IsFalse(result.ModelAdapterResult.Response.ClaimedSafetyFlags.MayMutateSource);
        Assert.IsFalse(result.ModelAdapterResult.Response.ClaimedSafetyFlags.MaySubmitGitHubReview);
        Assert.IsFalse(result.ModelAdapterResult.Response.ClaimedSafetyFlags.MayPromoteMemory);
        Assert.IsFalse(result.ModelAdapterResult.Audit.GrantsApproval);
        Assert.IsFalse(result.ModelAdapterResult.Audit.GrantsAuthority);
        Assert.IsFalse(result.ModelAdapterResult.Audit.GrantsMemoryPromotion);
        Assert.IsNotNull(result.SanitisationResult?.Prompt);
        Assert.IsNotNull(result.SanitisationResult.Response);
        Assert.IsNotNull(result.SanitisationResult.Audit);
        Assert.IsNull(result.SanitisationResult.Prompt.RedactedPreview);
        Assert.IsNull(result.SanitisationResult.Response.RedactedPreview);
        Assert.IsFalse(result.SanitisationResult.Response.ContainsAuthorityClaim);
        Assert.IsFalse(result.SanitisationResult.Response.ContainsToolCommand);
        Assert.IsFalse(result.SanitisationResult.Response.ContainsSourceMutationCommand);
        Assert.IsFalse(result.SanitisationResult.Response.ContainsMemoryPromotionCommand);
        Assert.IsFalse(result.SanitisationResult.Audit.GrantsAuthority);
        Assert.IsFalse(result.SanitisationResult.Audit.GrantsApproval);
        Assert.IsFalse(result.SanitisationResult.Audit.GrantsMemoryPromotion);
    }

    private static void AssertCapability(
        IReadOnlyList<AgentCapabilityUseRecord> uses,
        AgentCapability capability,
        AgentCapabilityUseOutcome outcome) =>
        Assert.IsTrue(uses.Any(use => use.Capability == capability && use.Outcome == outcome), $"{capability} {outcome}");

    private static void AssertNoIssues<TIssue>(IReadOnlyList<TIssue> issues)
        where TIssue : class
    {
        Assert.AreEqual(0, issues.Count, string.Join(Environment.NewLine, issues.Select(issue => issue.ToString())));
    }

    private static void AssertHasIssue(IReadOnlyList<ModelBackedCriticReviewIssue> issues, string code) =>
        Assert.IsTrue(issues.Any(issue => string.Equals(issue.Code, code, StringComparison.Ordinal)), FormatIssues(issues));

    private static string FormatIssues(IEnumerable<ModelBackedCriticReviewIssue> issues) =>
        string.Join(Environment.NewLine, issues.Select(issue => $"{issue.Code}: {issue.Message}"));

    private static string ReadRepositoryFile(params string[] pathParts)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", Path.Combine(pathParts));
        return File.ReadAllText(Path.GetFullPath(path));
    }

    private sealed class CountingAdapter : IAgentModelAdapter
    {
        private readonly Func<AgentModelRequest, DateTimeOffset, AgentModelAdapterResult> _resultFactory;

        public CountingAdapter(Func<AgentModelRequest, DateTimeOffset, AgentModelAdapterResult> resultFactory)
        {
            _resultFactory = resultFactory;
        }

        public int CallCount { get; private set; }

        public AgentModelAdapterResult Invoke(AgentModelRequest request, DateTimeOffset invokedAtUtc)
        {
            CallCount++;
            return _resultFactory(request, invokedAtUtc);
        }
    }

    private sealed class UnsafeSucceededAdapter : IAgentModelAdapter
    {
        public AgentModelAdapterResult Invoke(AgentModelRequest request, DateTimeOffset invokedAtUtc)
        {
            var response = SafeResponse(request, invokedAtUtc, SafeCriticJson()) with
            {
                Content = "approved for execution",
                ContainsAuthorityClaim = true
            };

            return new AgentModelAdapterResult
            {
                Succeeded = true,
                Response = response,
                Audit = SafeAudit(request, response, invokedAtUtc)
            };
        }
    }
}
