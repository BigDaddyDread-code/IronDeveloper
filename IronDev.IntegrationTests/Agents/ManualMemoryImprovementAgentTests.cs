using IronDev.Core.Agents;
using IronDev.Core.Agents.Concrete;
using Audit = IronDev.Core.Agents.Audit;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Agents;

[TestClass]
public sealed class ManualMemoryImprovementAgentTests
{
    private static readonly DateTimeOffset DetectedAt = new(2026, 6, 11, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void ManualMemoryImprovementAgent_ContractsExist()
    {
        Assert.AreEqual(nameof(ManualMemoryImprovementDetectionRequest), typeof(ManualMemoryImprovementDetectionRequest).Name);
        Assert.AreEqual(nameof(ManualMemoryImprovementInputRef), typeof(ManualMemoryImprovementInputRef).Name);
        Assert.AreEqual(nameof(ManualMemoryImprovementPatternDraft), typeof(ManualMemoryImprovementPatternDraft).Name);
        Assert.AreEqual(nameof(ManualMemoryImprovementProposalDraftInput), typeof(ManualMemoryImprovementProposalDraftInput).Name);
        Assert.AreEqual(nameof(ManualMemoryImprovementDetectionResult), typeof(ManualMemoryImprovementDetectionResult).Name);
        Assert.AreEqual(nameof(ManualMemoryImprovementIssue), typeof(ManualMemoryImprovementIssue).Name);
        Assert.AreEqual(nameof(IManualMemoryImprovementAgentService), typeof(IManualMemoryImprovementAgentService).Name);
        Assert.AreEqual(nameof(ManualMemoryImprovementAgentService), typeof(ManualMemoryImprovementAgentService).Name);
        Assert.AreEqual(nameof(ManualMemoryImprovementDetectionValidator), typeof(ManualMemoryImprovementDetectionValidator).Name);
    }

    [TestMethod]
    public void ManualMemoryImprovementAgent_ValidRequestProducesDetectionResultAndAuditEnvelope()
    {
        var result = Detect(BuildRequest());

        Assert.IsTrue(result.Succeeded, FormatIssues(result.Issues));
        Assert.IsNotNull(result.DetectionResult);
        Assert.IsNotNull(result.AuditEnvelope);
        Assert.AreEqual("memory-detection-detection-1", result.DetectionResult.DetectionResultId);
        Assert.AreEqual("builtin.memory-improvement", result.DetectionResult.DetectedByAgentId);
        Assert.AreEqual("corr-1", result.DetectionResult.CorrelationId);
        AssertNoIssues(new MemoryImprovementDetectionResultValidator().Validate(result.DetectionResult));
        AssertNoIssues(new Audit.AgentRunAuditEnvelopeValidator().Validate(result.AuditEnvelope));
        Assert.AreEqual(AgentDefinitionCatalog.MemoryImprovementAgent.AgentId, result.AuditEnvelope.AgentDefinitionSnapshot.AgentId);
        Assert.AreEqual(Audit.AgentRunTriggerType.ManualUserRequest, result.AuditEnvelope.Run.TriggerType);
        Assert.AreEqual(Audit.AgentRunStatus.CompletedWithWarnings, result.AuditEnvelope.Run.Status);
        Assert.AreEqual("user-1", result.AuditEnvelope.Run.RequestedByUserId);
    }

    [TestMethod]
    public void ManualMemoryImprovementAgent_DetectionOutputIsProposalOnly()
    {
        var output = Detect(BuildRequest()).AuditEnvelope!.Outputs.Single(item => item.RefType == "MemoryImprovementDetectionResult");

        Assert.IsFalse(output.IsReviewOnly);
        Assert.IsTrue(output.IsProposalOnly);
        Assert.IsFalse(output.CreatesAuthority);
        Assert.IsFalse(output.CreatesRuntimeAction);
        Assert.IsFalse(output.ContainsRawPrivateReasoning);
    }

    [TestMethod]
    public void ManualMemoryImprovementAgent_ProposalDraftsAreProposalOnlyAndDoNotCreateOrPromoteMemory()
    {
        var proposal = Detect(BuildRequest()).DetectionResult!.ProposalDrafts.Single();
        var output = Detect(BuildRequest()).AuditEnvelope!.Outputs.Single(item => item.RefType == "MemoryImprovementProposalDraft");

        Assert.IsTrue(proposal.IsProposalOnly);
        Assert.IsFalse(proposal.CreatesCollectiveMemory);
        Assert.IsFalse(proposal.PromotesMemory);
        Assert.IsTrue(proposal.RequiresHumanReview);
        Assert.IsTrue(output.IsProposalOnly);
        Assert.IsFalse(output.CreatesAuthority);
        Assert.IsFalse(output.CreatesRuntimeAction);
    }

    [TestMethod]
    public void ManualMemoryImprovementAgent_ThoughtLedgerContainsRequiredSafeEntries()
    {
        var ledger = Detect(BuildRequest()).AuditEnvelope!.ThoughtLedger;

        CollectionAssert.Contains(ledger.Select(entry => entry.EntryType).Cast<object>().ToArray(), Audit.ThoughtLedgerEntryType.EvidenceUsed);
        CollectionAssert.Contains(ledger.Select(entry => entry.EntryType).Cast<object>().ToArray(), Audit.ThoughtLedgerEntryType.Assumption);
        CollectionAssert.Contains(ledger.Select(entry => entry.EntryType).Cast<object>().ToArray(), Audit.ThoughtLedgerEntryType.RejectedAlternative);
        CollectionAssert.Contains(ledger.Select(entry => entry.EntryType).Cast<object>().ToArray(), Audit.ThoughtLedgerEntryType.BoundaryDecision);
        CollectionAssert.Contains(ledger.Select(entry => entry.EntryType).Cast<object>().ToArray(), Audit.ThoughtLedgerEntryType.OutputRationale);
        Assert.IsTrue(ledger.All(entry => !entry.ContainsRawPrivateReasoning));
        Assert.IsTrue(ledger.All(entry => !entry.GrantsApproval));
        Assert.IsTrue(ledger.All(entry => !entry.GrantsAuthority));
        Assert.IsTrue(ledger.All(entry => !entry.GrantsMemoryPromotion));
        AssertNoIssues(new Audit.ThoughtLedgerSafetyValidator().Validate(ledger));
    }

    [TestMethod]
    public void ManualMemoryImprovementAgent_NoProposalReasonCanSkipPatternAndProposalDrafts()
    {
        var result = Detect(BuildRequest(
            patternDrafts: [],
            proposalDrafts: [],
            noProposalReason: MemoryImprovementNoProposalReason.InsufficientEvidence));

        Assert.IsTrue(result.Succeeded, FormatIssues(result.Issues));
        Assert.AreEqual(MemoryImprovementNoProposalReason.InsufficientEvidence, result.DetectionResult!.NoProposalReason);
        Assert.AreEqual(0, result.DetectionResult.Findings.Count);
        Assert.AreEqual(0, result.DetectionResult.ProposalDrafts.Count);
        Assert.IsNotNull(result.AuditEnvelope);
        Assert.AreEqual(Audit.AgentRunStatus.Completed, result.AuditEnvelope!.Run.Status);
        Assert.IsFalse(result.AuditEnvelope.Outputs.Single().CreatesAuthority);
        Assert.IsFalse(result.AuditEnvelope.Outputs.Single().CreatesRuntimeAction);
        AssertNoIssues(new Audit.AgentRunAuditEnvelopeValidator().Validate(result.AuditEnvelope));
    }

    [TestMethod]
    public void ManualMemoryImprovementAgent_AuditRecordsBlockedDangerousCapabilities()
    {
        var uses = Detect(BuildRequest()).AuditEnvelope!.CapabilityUses;

        AssertCapability(uses, AgentCapability.CreateMemoryProposal, Audit.AgentCapabilityUseOutcome.Allowed);
        AssertCapability(uses, AgentCapability.CreateReport, Audit.AgentCapabilityUseOutcome.Allowed);
        AssertCapability(uses, AgentCapability.PromoteCollectiveMemory, Audit.AgentCapabilityUseOutcome.Blocked);
        AssertCapability(uses, AgentCapability.RunTool, Audit.AgentCapabilityUseOutcome.Blocked);
        AssertCapability(uses, AgentCapability.MutateSource, Audit.AgentCapabilityUseOutcome.Blocked);
        AssertCapability(uses, AgentCapability.CallExternalSystem, Audit.AgentCapabilityUseOutcome.Blocked);
        AssertCapability(uses, AgentCapability.BlockExecution, Audit.AgentCapabilityUseOutcome.Blocked);
        AssertCapability(uses, AgentCapability.RepresentHumanApproval, Audit.AgentCapabilityUseOutcome.Blocked);
        AssertCapability(uses, AgentCapability.RepresentHumanPromotionDecision, Audit.AgentCapabilityUseOutcome.Blocked);
    }

    [TestMethod]
    public void ManualMemoryImprovementAgent_AuditRecordsCapabilityBoundaryDecisions()
    {
        var decisions = Detect(BuildRequest()).AuditEnvelope!.BoundaryDecisions;

        Assert.IsTrue(decisions.Any(decision => decision.BoundaryType == Audit.AgentBoundaryDecisionType.AgentDefinition && decision.Decision == "allow"));
        Assert.IsTrue(decisions.Any(decision => decision.BoundaryType == Audit.AgentBoundaryDecisionType.Capability && decision.Decision == "allow" && decision.Reason.Contains("CreateMemoryProposal", StringComparison.Ordinal)));
        Assert.IsTrue(decisions.Any(decision => decision.BoundaryType == Audit.AgentBoundaryDecisionType.Capability && decision.Decision == "block" && decision.Reason.Contains("PromoteCollectiveMemory", StringComparison.Ordinal)));
        Assert.IsTrue(decisions.Any(decision => decision.BoundaryType == Audit.AgentBoundaryDecisionType.Capability && decision.Decision == "block" && decision.Reason.Contains("RunTool", StringComparison.Ordinal)));
        Assert.IsTrue(decisions.Any(decision => decision.BoundaryType == Audit.AgentBoundaryDecisionType.Capability && decision.Decision == "block" && decision.Reason.Contains("MutateSource", StringComparison.Ordinal)));
        Assert.IsTrue(decisions.Any(decision => decision.BoundaryType == Audit.AgentBoundaryDecisionType.Capability && decision.Decision == "block" && decision.Reason.Contains("CallExternalSystem", StringComparison.Ordinal)));
        Assert.IsTrue(decisions.Any(decision => decision.BoundaryType == Audit.AgentBoundaryDecisionType.Capability && decision.Decision == "block" && decision.Reason.Contains("BlockExecution", StringComparison.Ordinal)));
        Assert.IsTrue(decisions.Any(decision => decision.BoundaryType == Audit.AgentBoundaryDecisionType.OutputValidation && decision.Decision == "allow"));
        Assert.IsTrue(decisions.Any(decision => decision.BoundaryType == Audit.AgentBoundaryDecisionType.ThoughtLedgerSafety && decision.Decision == "allow"));
        Assert.IsTrue(decisions.All(decision => !decision.GrantsAuthority));
        Assert.IsTrue(decisions.All(decision => !decision.GrantsHumanApproval));
        Assert.IsTrue(decisions.All(decision => !decision.GrantsPolicyApproval));
        Assert.IsTrue(decisions.All(decision => !decision.GrantsMemoryPromotion));
    }

    [TestMethod]
    public void ManualMemoryImprovementValidator_RejectsMissingRequiredFields()
    {
        var issues = new ManualMemoryImprovementDetectionValidator().Validate(BuildRequest() with
        {
            DetectionRequestId = string.Empty,
            TenantId = string.Empty,
            ProjectId = string.Empty,
            CampaignId = string.Empty,
            RunId = string.Empty,
            RequestedByUserId = string.Empty
        });

        AssertHasIssue(issues, ManualMemoryImprovementDetectionValidator.DetectionRequestIdRequired);
        AssertHasIssue(issues, ManualMemoryImprovementDetectionValidator.ScopeRequired);
        AssertHasIssue(issues, ManualMemoryImprovementDetectionValidator.RequestedByUserIdRequired);
    }

    [TestMethod]
    public void ManualMemoryImprovementValidator_RejectsMissingInputAndPatternWithoutNoProposalReason()
    {
        var issues = new ManualMemoryImprovementDetectionValidator().Validate(BuildRequest(inputs: [], patternDrafts: []));

        AssertHasIssue(issues, ManualMemoryImprovementDetectionValidator.InputRequired);
        AssertHasIssue(issues, ManualMemoryImprovementDetectionValidator.PatternRequired);
    }

    [TestMethod]
    public void ManualMemoryImprovementValidator_RejectsInvalidPatternDraft()
    {
        var issues = new ManualMemoryImprovementDetectionValidator().Validate(BuildRequest(patternDrafts:
        [
            BuildPattern() with
            {
                PatternType = (MemoryImprovementPatternType)999,
                Summary = string.Empty,
                Confidence = -0.1m,
                RequiresHumanReview = false,
                EvidenceRefs = [string.Empty]
            },
            BuildPattern() with
            {
                Confidence = 1.1m
            }
        ]));

        AssertHasIssue(issues, ManualMemoryImprovementDetectionValidator.PatternTypeInvalid);
        AssertHasIssue(issues, ManualMemoryImprovementDetectionValidator.PatternSummaryRequired);
        AssertHasIssue(issues, ManualMemoryImprovementDetectionValidator.PatternConfidenceInvalid);
        AssertHasIssue(issues, ManualMemoryImprovementDetectionValidator.PatternHumanReviewRequired);
        AssertHasIssue(issues, ManualMemoryImprovementDetectionValidator.BlankEvidenceRef);
    }

    [TestMethod]
    public void ManualMemoryImprovementValidator_RejectsUnsafeProposalDraftInput()
    {
        var issues = new ManualMemoryImprovementDetectionValidator().Validate(BuildRequest(proposalDrafts:
        [
            BuildProposal() with
            {
                SourcePatternIndex = 99,
                Title = string.Empty,
                Summary = string.Empty,
                Rationale = string.Empty,
                EvidenceRefs = [],
                IsProposalOnly = false,
                CreatesCollectiveMemory = true,
                PromotesMemory = true,
                RequiresHumanReview = false
            }
        ]));

        AssertHasIssue(issues, ManualMemoryImprovementDetectionValidator.ProposalSourcePatternInvalid);
        AssertHasIssue(issues, ManualMemoryImprovementDetectionValidator.ProposalTitleRequired);
        AssertHasIssue(issues, ManualMemoryImprovementDetectionValidator.ProposalSummaryRequired);
        AssertHasIssue(issues, ManualMemoryImprovementDetectionValidator.ProposalRationaleRequired);
        AssertHasIssue(issues, ManualMemoryImprovementDetectionValidator.ProposalEvidenceRequired);
        AssertHasIssue(issues, ManualMemoryImprovementDetectionValidator.ProposalOnlyRequired);
        AssertHasIssue(issues, ManualMemoryImprovementDetectionValidator.CreatesCollectiveMemoryBlocked);
        AssertHasIssue(issues, ManualMemoryImprovementDetectionValidator.PromotesMemoryBlocked);
        AssertHasIssue(issues, ManualMemoryImprovementDetectionValidator.ProposalHumanReviewRequired);
    }

    [TestMethod]
    public void ManualMemoryImprovementValidator_RejectsRawPrivateReasoningMarkers()
    {
        foreach (var marker in new[]
        {
            "RawPrompt: hidden.",
            "RawCompletion: hidden.",
            "ChainOfThought: hidden.",
            "Scratchpad: hidden.",
            "PrivateReasoning: hidden."
        })
        {
            AssertHasIssue(
                new ManualMemoryImprovementDetectionValidator().Validate(BuildRequest(requestSummary: marker)),
                ManualMemoryImprovementDetectionValidator.RawPrivateReasoningBlocked);
        }
    }

    [TestMethod]
    public void ManualMemoryImprovementValidator_RejectsAuthorityApprovalAndPromotionClaims()
    {
        AssertHasIssue(
            new ManualMemoryImprovementDetectionValidator().Validate(BuildRequest(requestSummary: "I approve this action.")),
            ManualMemoryImprovementDetectionValidator.ApprovalClaimBlocked);
        AssertHasIssue(
            new ManualMemoryImprovementDetectionValidator().Validate(BuildRequest(requestSummary: "The human approved this.")),
            ManualMemoryImprovementDetectionValidator.ApprovalClaimBlocked);
        AssertHasIssue(
            new ManualMemoryImprovementDetectionValidator().Validate(BuildRequest(requestSummary: "This promoted memory.")),
            ManualMemoryImprovementDetectionValidator.MemoryPromotionClaimBlocked);
        AssertHasIssue(
            new ManualMemoryImprovementDetectionValidator().Validate(BuildRequest(requestSummary: "Policy cleared this action.")),
            ManualMemoryImprovementDetectionValidator.AuthorityClaimBlocked);
    }

    [TestMethod]
    public void ManualMemoryImprovementValidator_RejectsRawOrAuthoritativeInputs()
    {
        var issues = new ManualMemoryImprovementDetectionValidator().Validate(BuildRequest(inputs:
        [
            BuildInput() with
            {
                ContainsRawPrivateReasoning = true,
                IsAuthoritativeForAction = true,
                Summary = "RawPrompt: hidden."
            }
        ]));

        AssertHasIssue(issues, ManualMemoryImprovementDetectionValidator.RawPrivateReasoningBlocked);
        AssertHasIssue(issues, ManualMemoryImprovementDetectionValidator.InputAuthorityBlocked);
    }

    [TestMethod]
    public void ManualMemoryImprovementAgent_InvalidRequestReturnsIssuesWithoutOutput()
    {
        var result = Detect(BuildRequest(inputs: []));

        Assert.IsFalse(result.Succeeded);
        Assert.IsNull(result.DetectionResult);
        Assert.IsNull(result.AuditEnvelope);
        Assert.IsTrue(result.Issues.Count > 0);
    }

    [TestMethod]
    public void ManualMemoryImprovementAgent_DoesNotAddRuntimeSqlVectorStorePersistenceOrAgentWiring()
    {
        var repositoryRoot = FindRepositoryRoot();
        var productionFiles = new[]
        {
            Path.Combine(repositoryRoot, "IronDev.Core", "Agents", "Concrete", "ManualMemoryImprovementAgentService.cs")
        };

        var forbiddenTokens = new[]
        {
            "IAgentRuntime",
            "AgentRuntime",
            "AgentScheduler",
            "AgentOrchestrator",
            "AgentPromptRunner",
            "AgentToolRouter",
            "ExecuteAgentAsync",
            "RunAgentAsync",
            "IChatCompletion",
            "OpenAI",
            "Anthropic",
            "Gemini",
            "SqlConnection",
            "CREATE TABLE",
            "CREATE PROCEDURE",
            "Weaviate",
            "INSERT INTO",
            "UPDATE ",
            "DELETE ",
            "MERGE ",
            "AddHostedService",
            "IHostedService",
            "BackgroundService",
            "ICollectiveMemoryPromotionService",
            "SqlCollectiveMemoryPromotionService",
            "IMemoryImprovementProposalStore",
            "SqlMemoryImprovementProposalStore"
        };

        foreach (var file in productionFiles)
        {
            var text = File.ReadAllText(file);
            foreach (var token in forbiddenTokens)
            {
                Assert.IsFalse(text.Contains(token, StringComparison.Ordinal),
                    $"Manual memory-improvement production file contains forbidden runtime token '{token}': {file}");
            }
        }

        var runtimeRoots = new[] { "IronDev.Infrastructure", "IronDev.Api", "IronDev.Client", "tools" };
        var runtimeFiles = runtimeRoots
            .Select(root => Path.Combine(repositoryRoot, root))
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(file => !file.Contains(Path.Combine("bin"), StringComparison.OrdinalIgnoreCase))
            .Where(file => !file.Contains(Path.Combine("obj"), StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (var file in runtimeFiles)
        {
            var text = RemoveStoredManualMemoryWrapperNames(File.ReadAllText(file));
            Assert.IsFalse(text.Contains("ManualMemoryImprovementAgentService", StringComparison.Ordinal),
                $"Runtime file wires ManualMemoryImprovementAgentService: {file}");
            Assert.IsFalse(text.Contains("IManualMemoryImprovementAgentService", StringComparison.Ordinal),
                $"Runtime file wires IManualMemoryImprovementAgentService: {file}");
        }
    }

    private static string RemoveStoredManualMemoryWrapperNames(string text) =>
        text
            .Replace("IStoredManualMemoryImprovementAgentService", string.Empty, StringComparison.Ordinal)
            .Replace("StoredManualMemoryImprovementAgentService", string.Empty, StringComparison.Ordinal);

    private static ManualMemoryImprovementDetectionResult Detect(ManualMemoryImprovementDetectionRequest request) =>
        new ManualMemoryImprovementAgentService().Detect(request, DetectedAt);

    private static ManualMemoryImprovementDetectionRequest BuildRequest(
        IReadOnlyList<ManualMemoryImprovementInputRef>? inputs = null,
        IReadOnlyList<ManualMemoryImprovementPatternDraft>? patternDrafts = null,
        IReadOnlyList<ManualMemoryImprovementProposalDraftInput>? proposalDrafts = null,
        MemoryImprovementNoProposalReason? noProposalReason = null,
        string requestSummary = "Review supplied memory evidence for repeated patterns.") =>
        new()
        {
            DetectionRequestId = "detection-1",
            TenantId = "tenant-1",
            ProjectId = "project-1",
            CampaignId = "campaign-1",
            RunId = "run-1",
            RequestedByUserId = "user-1",
            CorrelationId = "corr-1",
            RequestSummary = requestSummary,
            Inputs = inputs ?? [BuildInput()],
            PatternDrafts = patternDrafts ?? [BuildPattern()],
            ProposalDrafts = proposalDrafts ?? [BuildProposal()],
            NoProposalReason = noProposalReason
        };

    private static ManualMemoryImprovementInputRef BuildInput() =>
        new()
        {
            InputRefId = "input-1",
            RefType = "AgentRunAuditEnvelope",
            RefId = "audit-1",
            Source = "manual test",
            Summary = "Audit evidence supplied by caller.",
            EvidenceRefs = ["evidence-1"]
        };

    private static ManualMemoryImprovementPatternDraft BuildPattern() =>
        new()
        {
            PatternType = MemoryImprovementPatternType.RepeatedGovernanceBlock,
            Summary = "Repeated approval-boundary confusion appears in three reviews.",
            Confidence = 0.82m,
            EvidenceRefs = ["evidence-1"],
            RelatedMemoryIds = ["memory-1"],
            RelatedProposalIds = ["proposal-1"],
            RequiresHumanReview = true
        };

    private static ManualMemoryImprovementProposalDraftInput BuildProposal() =>
        new()
        {
            Title = "Clarify approval evidence boundary",
            Summary = "Draft a proposal reminding agents that evidence is accountability, not authority.",
            Rationale = "Repeated reviews show the same confusion.",
            SourcePatternIndex = 0,
            EvidenceRefs = ["evidence-1"],
            IsProposalOnly = true,
            CreatesCollectiveMemory = false,
            PromotesMemory = false,
            RequiresHumanReview = true
        };

    private static void AssertCapability(
        IReadOnlyList<Audit.AgentCapabilityUseRecord> uses,
        AgentCapability capability,
        Audit.AgentCapabilityUseOutcome outcome)
    {
        var use = uses.SingleOrDefault(item => item.Capability == capability);
        Assert.IsNotNull(use, $"Missing capability use for {capability}.");
        Assert.AreEqual(outcome, use.Outcome);
    }

    private static void AssertHasIssue(IReadOnlyList<ManualMemoryImprovementIssue> issues, string code)
    {
        Assert.IsTrue(
            issues.Any(issue => string.Equals(issue.Code, code, StringComparison.Ordinal)),
            $"Expected validation issue '{code}' but got: {string.Join(", ", issues.Select(issue => issue.Code))}");
    }

    private static void AssertNoIssues(IReadOnlyList<AgentDefinitionValidationIssue> issues)
    {
        Assert.AreEqual(
            0,
            issues.Count,
            $"Expected no validation issues but got: {string.Join(", ", issues.Select(issue => $"{issue.Code}:{issue.Message}"))}");
    }

    private static string FormatIssues(IReadOnlyList<ManualMemoryImprovementIssue> issues) =>
        string.Join("; ", issues.Select(issue => $"{issue.Code}:{issue.Message}"));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "IronDev.Core")) &&
                Directory.Exists(Path.Combine(directory.FullName, "IronDev.Infrastructure")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate AIDeveloper repository root.");
    }
}
