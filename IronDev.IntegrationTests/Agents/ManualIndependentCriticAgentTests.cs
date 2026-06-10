using IronDev.Core.Agents;
using IronDev.Core.Agents.Concrete;
using Audit = IronDev.Core.Agents.Audit;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Agents;

[TestClass]
public sealed class ManualIndependentCriticAgentTests
{
    private static readonly DateTimeOffset ReviewedAt = new(2026, 6, 10, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void ManualIndependentCriticAgent_ContractsExist()
    {
        Assert.AreEqual(nameof(ManualCriticReviewRequest), typeof(ManualCriticReviewRequest).Name);
        Assert.AreEqual(nameof(ManualCriticReviewInputRef), typeof(ManualCriticReviewInputRef).Name);
        Assert.AreEqual(nameof(ManualCriticFindingDraft), typeof(ManualCriticFindingDraft).Name);
        Assert.AreEqual(nameof(ManualCriticReviewResult), typeof(ManualCriticReviewResult).Name);
        Assert.AreEqual(nameof(ManualCriticReviewIssue), typeof(ManualCriticReviewIssue).Name);
        Assert.AreEqual(nameof(IManualIndependentCriticAgentService), typeof(IManualIndependentCriticAgentService).Name);
        Assert.AreEqual(nameof(ManualIndependentCriticAgentService), typeof(ManualIndependentCriticAgentService).Name);
        Assert.AreEqual(nameof(ManualCriticReviewValidator), typeof(ManualCriticReviewValidator).Name);
    }

    [TestMethod]
    public void ManualIndependentCriticAgent_ValidRequestProducesCriticResultAndAuditEnvelope()
    {
        var result = Review(BuildRequest());

        Assert.IsTrue(result.Succeeded, FormatIssues(result.Issues));
        Assert.IsNotNull(result.CriticReviewResult);
        Assert.IsNotNull(result.AuditEnvelope);
        Assert.AreEqual("critic-review-review-1", result.CriticReviewResult.ReviewResultId);
        Assert.AreEqual("builtin.independent-critic", result.CriticReviewResult.ReviewedByAgentId);
        Assert.AreEqual("corr-1", result.CriticReviewResult.CorrelationId);
        AssertNoIssues(new CriticReviewResultValidator().Validate(result.CriticReviewResult));
        AssertNoIssues(new Audit.AgentRunAuditEnvelopeValidator().Validate(result.AuditEnvelope));
        Assert.AreEqual(AgentDefinitionCatalog.IndependentCriticAgent.AgentId, result.AuditEnvelope.AgentDefinitionSnapshot.AgentId);
        Assert.AreEqual(Audit.AgentRunTriggerType.ManualUserRequest, result.AuditEnvelope.Run.TriggerType);
        Assert.AreEqual(Audit.AgentRunStatus.CompletedWithWarnings, result.AuditEnvelope.Run.Status);
        Assert.AreEqual("user-1", result.AuditEnvelope.Run.RequestedByUserId);
    }

    [TestMethod]
    public void ManualIndependentCriticAgent_AuditOutputIsCriticReviewResultAndReviewOnly()
    {
        var output = Review(BuildRequest()).AuditEnvelope!.Outputs.Single();

        Assert.AreEqual("CriticReviewResult", output.RefType);
        Assert.IsTrue(output.IsReviewOnly);
        Assert.IsFalse(output.IsProposalOnly);
        Assert.IsFalse(output.CreatesAuthority);
        Assert.IsFalse(output.CreatesRuntimeAction);
        Assert.IsFalse(output.ContainsRawPrivateReasoning);
    }

    [TestMethod]
    public void ManualIndependentCriticAgent_ThoughtLedgerContainsRequiredSafeEntries()
    {
        var ledger = Review(BuildRequest()).AuditEnvelope!.ThoughtLedger;

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
    public void ManualIndependentCriticAgent_AuditRecordsBlockedDangerousCapabilities()
    {
        var uses = Review(BuildRequest()).AuditEnvelope!.CapabilityUses;

        AssertCapability(uses, AgentCapability.CreateCriticFinding, Audit.AgentCapabilityUseOutcome.Allowed);
        AssertCapability(uses, AgentCapability.CreateReport, Audit.AgentCapabilityUseOutcome.Allowed);
        AssertCapability(uses, AgentCapability.WarnExecution, Audit.AgentCapabilityUseOutcome.Allowed);
        AssertCapability(uses, AgentCapability.BlockExecution, Audit.AgentCapabilityUseOutcome.Blocked);
        AssertCapability(uses, AgentCapability.RunTool, Audit.AgentCapabilityUseOutcome.Blocked);
        AssertCapability(uses, AgentCapability.MutateSource, Audit.AgentCapabilityUseOutcome.Blocked);
        AssertCapability(uses, AgentCapability.CallExternalSystem, Audit.AgentCapabilityUseOutcome.Blocked);
        AssertCapability(uses, AgentCapability.PromoteCollectiveMemory, Audit.AgentCapabilityUseOutcome.Blocked);
        AssertCapability(uses, AgentCapability.RepresentHumanApproval, Audit.AgentCapabilityUseOutcome.Blocked);
        AssertCapability(uses, AgentCapability.RepresentHumanPromotionDecision, Audit.AgentCapabilityUseOutcome.Blocked);
    }

    [TestMethod]
    public void ManualIndependentCriticAgent_AuditRecordsCapabilityBoundaryDecisions()
    {
        var decisions = Review(BuildRequest()).AuditEnvelope!.BoundaryDecisions;

        Assert.IsTrue(decisions.Any(decision => decision.BoundaryType == Audit.AgentBoundaryDecisionType.AgentDefinition && decision.Decision == "allow"));
        Assert.IsTrue(decisions.Any(decision => decision.BoundaryType == Audit.AgentBoundaryDecisionType.Capability && decision.Decision == "allow" && decision.Reason.Contains("CreateCriticFinding", StringComparison.Ordinal)));
        Assert.IsTrue(decisions.Any(decision => decision.BoundaryType == Audit.AgentBoundaryDecisionType.Capability && decision.Decision == "block" && decision.Reason.Contains("BlockExecution", StringComparison.Ordinal)));
        Assert.IsTrue(decisions.Any(decision => decision.BoundaryType == Audit.AgentBoundaryDecisionType.Capability && decision.Decision == "block" && decision.Reason.Contains("RunTool", StringComparison.Ordinal)));
        Assert.IsTrue(decisions.Any(decision => decision.BoundaryType == Audit.AgentBoundaryDecisionType.Capability && decision.Decision == "block" && decision.Reason.Contains("MutateSource", StringComparison.Ordinal)));
        Assert.IsTrue(decisions.Any(decision => decision.BoundaryType == Audit.AgentBoundaryDecisionType.OutputValidation && decision.Decision == "allow"));
        Assert.IsTrue(decisions.Any(decision => decision.BoundaryType == Audit.AgentBoundaryDecisionType.ThoughtLedgerSafety && decision.Decision == "allow"));
        Assert.IsTrue(decisions.All(decision => !decision.GrantsAuthority));
        Assert.IsTrue(decisions.All(decision => !decision.GrantsHumanApproval));
        Assert.IsTrue(decisions.All(decision => !decision.GrantsPolicyApproval));
        Assert.IsTrue(decisions.All(decision => !decision.GrantsMemoryPromotion));
    }

    [TestMethod]
    public void ManualIndependentCriticAgent_RecommendBlockIsReviewOnlyNotAuthorityOrRuntimeAction()
    {
        var request = BuildRequest(verdict: CriticReviewVerdict.RecommendBlock);
        var result = Review(request);

        Assert.IsTrue(result.Succeeded, FormatIssues(result.Issues));
        Assert.AreEqual(CriticReviewVerdict.RecommendBlock, result.CriticReviewResult!.Verdict);
        Assert.IsTrue(result.CriticReviewResult.Findings.Single().BlocksMerge);
        Assert.IsFalse(result.AuditEnvelope!.Outputs.Single().CreatesAuthority);
        Assert.IsFalse(result.AuditEnvelope.Outputs.Single().CreatesRuntimeAction);
        Assert.IsTrue(result.AuditEnvelope.BoundaryDecisions.All(decision => !decision.GrantsAuthority));
    }

    [TestMethod]
    public void ManualIndependentCriticAgent_NoObjectionCanHaveNoFindings()
    {
        var result = Review(BuildRequest(
            verdict: CriticReviewVerdict.NoObjection,
            findings: []));

        Assert.IsTrue(result.Succeeded, FormatIssues(result.Issues));
        Assert.AreEqual(CriticReviewVerdict.NoObjection, result.CriticReviewResult!.Verdict);
        Assert.AreEqual(0, result.CriticReviewResult.Findings.Count);
        Assert.AreEqual(Audit.AgentRunStatus.Completed, result.AuditEnvelope!.Run.Status);
    }

    [TestMethod]
    public void ManualCriticReviewValidator_RejectsMissingRequiredFields()
    {
        var issues = new ManualCriticReviewValidator().Validate(BuildRequest() with
        {
            ReviewRequestId = string.Empty,
            TenantId = string.Empty,
            ProjectId = string.Empty,
            CampaignId = string.Empty,
            RunId = string.Empty,
            SubjectId = string.Empty,
            RequestedByUserId = string.Empty,
            SubjectType = (CriticReviewSubjectType)999
        });

        AssertHasIssue(issues, ManualCriticReviewValidator.ReviewRequestIdRequired);
        AssertHasIssue(issues, ManualCriticReviewValidator.ScopeRequired);
        AssertHasIssue(issues, ManualCriticReviewValidator.SubjectIdRequired);
        AssertHasIssue(issues, ManualCriticReviewValidator.RequestedByUserIdRequired);
        AssertHasIssue(issues, ManualCriticReviewValidator.SubjectTypeInvalid);
    }

    [TestMethod]
    public void ManualCriticReviewValidator_RejectsMissingInputAndFindingForRequestChanges()
    {
        var issues = new ManualCriticReviewValidator().Validate(BuildRequest(inputs: [], findings: []));

        AssertHasIssue(issues, ManualCriticReviewValidator.InputRequired);
        AssertHasIssue(issues, ManualCriticReviewValidator.FindingRequired);
    }

    [TestMethod]
    public void ManualCriticReviewValidator_RejectsNoObjectionWithBlockingFinding()
    {
        var issues = new ManualCriticReviewValidator().Validate(BuildRequest(verdict: CriticReviewVerdict.NoObjection));

        AssertHasIssue(issues, ManualCriticReviewValidator.NoObjectionCannotBlock);
    }

    [TestMethod]
    public void ManualCriticReviewValidator_RejectsRecommendBlockWithoutBlockingFinding()
    {
        var issues = new ManualCriticReviewValidator().Validate(BuildRequest(
            verdict: CriticReviewVerdict.RecommendBlock,
            findings: [BuildFinding(blocksMerge: false)]));

        AssertHasIssue(issues, ManualCriticReviewValidator.RecommendBlockRequiresBlockingFinding);
    }

    [TestMethod]
    public void ManualCriticReviewValidator_RejectsFindingMissingFields()
    {
        var issues = new ManualCriticReviewValidator().Validate(BuildRequest(findings:
        [
            BuildFinding() with
            {
                Severity = (CriticSeverity)999,
                Title = string.Empty,
                Problem = string.Empty,
                WhyItMatters = string.Empty,
                RequiredFix = string.Empty
            }
        ]));

        AssertHasIssue(issues, ManualCriticReviewValidator.FindingSeverityInvalid);
        AssertHasIssue(issues, ManualCriticReviewValidator.FindingTitleRequired);
        AssertHasIssue(issues, ManualCriticReviewValidator.FindingProblemRequired);
        AssertHasIssue(issues, ManualCriticReviewValidator.FindingWhyItMattersRequired);
        AssertHasIssue(issues, ManualCriticReviewValidator.FindingRequiredFixRequired);
    }

    [TestMethod]
    public void ManualCriticReviewValidator_RejectsRawPrivateReasoningMarkers()
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
                new ManualCriticReviewValidator().Validate(BuildRequest(requestSummary: marker)),
                ManualCriticReviewValidator.RawPrivateReasoningBlocked);
        }
    }

    [TestMethod]
    public void ManualCriticReviewValidator_RejectsAuthorityApprovalAndPromotionClaims()
    {
        AssertHasIssue(
            new ManualCriticReviewValidator().Validate(BuildRequest(requestSummary: "I approve this action.")),
            ManualCriticReviewValidator.ApprovalClaimBlocked);
        AssertHasIssue(
            new ManualCriticReviewValidator().Validate(BuildRequest(requestSummary: "The human approved this.")),
            ManualCriticReviewValidator.ApprovalClaimBlocked);
        AssertHasIssue(
            new ManualCriticReviewValidator().Validate(BuildRequest(requestSummary: "This promoted memory.")),
            ManualCriticReviewValidator.MemoryPromotionClaimBlocked);
        AssertHasIssue(
            new ManualCriticReviewValidator().Validate(BuildRequest(requestSummary: "Policy cleared this action.")),
            ManualCriticReviewValidator.AuthorityClaimBlocked);
    }

    [TestMethod]
    public void ManualCriticReviewValidator_RejectsRawOrAuthoritativeInputs()
    {
        var issues = new ManualCriticReviewValidator().Validate(BuildRequest(inputs:
        [
            BuildInput() with
            {
                ContainsRawPrivateReasoning = true,
                IsAuthoritativeForAction = true,
                Summary = "RawPrompt: hidden."
            }
        ]));

        AssertHasIssue(issues, ManualCriticReviewValidator.RawPrivateReasoningBlocked);
        AssertHasIssue(issues, ManualCriticReviewValidator.InputAuthorityBlocked);
    }

    [TestMethod]
    public void ManualIndependentCriticAgent_InvalidRequestReturnsIssuesWithoutOutput()
    {
        var result = Review(BuildRequest(inputs: []));

        Assert.IsFalse(result.Succeeded);
        Assert.IsNull(result.CriticReviewResult);
        Assert.IsNull(result.AuditEnvelope);
        Assert.IsTrue(result.Issues.Count > 0);
    }

    [TestMethod]
    public void ManualIndependentCriticAgent_DoesNotAddRuntimeSqlWeaviateGithubOrAgentWiring()
    {
        var repositoryRoot = FindRepositoryRoot();
        var productionFiles = new[]
        {
            Path.Combine(repositoryRoot, "IronDev.Core", "Agents", "Concrete", "ManualIndependentCriticAgentService.cs"),
            Path.Combine(repositoryRoot, "IronDev.Core", "Agents", "Audit", "AgentRunAuditModels.cs")
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
            "Octokit",
            "GitHubClient",
            "CreateReview",
            "SubmitReview"
        };

        foreach (var file in productionFiles)
        {
            var text = File.ReadAllText(file);
            foreach (var token in forbiddenTokens)
            {
                Assert.IsFalse(text.Contains(token, StringComparison.Ordinal),
                    $"Manual critic production file contains forbidden runtime token '{token}': {file}");
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
            var text = File.ReadAllText(file);
            Assert.IsFalse(text.Contains("ManualIndependentCriticAgentService", StringComparison.Ordinal),
                $"Runtime file wires ManualIndependentCriticAgentService: {file}");
            Assert.IsFalse(text.Contains("IManualIndependentCriticAgentService", StringComparison.Ordinal),
                $"Runtime file wires IManualIndependentCriticAgentService: {file}");
        }
    }

    private static ManualCriticReviewResult Review(ManualCriticReviewRequest request) =>
        new ManualIndependentCriticAgentService().Review(request, ReviewedAt);

    private static ManualCriticReviewRequest BuildRequest(
        CriticReviewVerdict verdict = CriticReviewVerdict.RequestChanges,
        IReadOnlyList<ManualCriticReviewInputRef>? inputs = null,
        IReadOnlyList<ManualCriticFindingDraft>? findings = null,
        string requestSummary = "Review the supplied diff and report evidence.") =>
        new()
        {
            ReviewRequestId = "review-1",
            TenantId = "tenant-1",
            ProjectId = "project-1",
            CampaignId = "campaign-1",
            RunId = "run-1",
            SubjectType = CriticReviewSubjectType.PullRequest,
            SubjectId = "pr-1",
            RequestedByUserId = "user-1",
            CorrelationId = "corr-1",
            RequestSummary = requestSummary,
            Inputs = inputs ?? [BuildInput()],
            FindingDrafts = findings ?? [BuildFinding()],
            RequestedVerdict = verdict
        };

    private static ManualCriticReviewInputRef BuildInput() =>
        new()
        {
            InputRefId = "input-1",
            RefType = "PullRequestDiff",
            RefId = "diff-1",
            Source = "manual test",
            Summary = "Diff evidence supplied by caller.",
            EvidenceRefs = ["evidence-1"]
        };

    private static ManualCriticFindingDraft BuildFinding(bool blocksMerge = true) =>
        new()
        {
            Severity = CriticSeverity.High,
            Title = "Missing boundary evidence",
            Problem = "The change does not prove the review boundary.",
            WhyItMatters = "Without evidence, the manual critic output could be mistaken for governance.",
            RequiredFix = "Add boundary evidence before merge.",
            EvidenceRefs = ["evidence-1"],
            BlocksMerge = blocksMerge,
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

    private static void AssertHasIssue(IReadOnlyList<ManualCriticReviewIssue> issues, string code)
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

    private static string FormatIssues(IReadOnlyList<ManualCriticReviewIssue> issues) =>
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
