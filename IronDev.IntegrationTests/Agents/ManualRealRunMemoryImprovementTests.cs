using IronDev.Core.Agents;
using IronDev.Core.Agents.Concrete;
using Audit = IronDev.Core.Agents.Audit;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Agents;

[TestClass]
public sealed class ManualRealRunMemoryImprovementTests
{
    private static readonly DateTimeOffset RequestedAt = new(2026, 6, 11, 23, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void ManualRealRunMemoryImprovementContracts_ExposeProposalOnlyShape()
    {
        Assert.IsNotNull(typeof(IManualRealRunMemoryImprovementService));
        Assert.IsNotNull(typeof(ManualRealRunMemoryImprovementService));
        Assert.IsNotNull(typeof(ManualRealRunMemoryImprovementRequest));
        Assert.IsNotNull(typeof(ManualRealRunMemoryImprovementResult));
        Assert.IsNotNull(typeof(ManualRealRunMemoryImprovementStatus));
        Assert.IsNotNull(typeof(RealRunEvidenceBundle));
        Assert.IsNotNull(typeof(RealRunEvidenceItem));
        Assert.IsNotNull(typeof(RealRunMemoryPattern));
        Assert.IsNotNull(typeof(RealRunMemoryImprovementCandidate));
        Assert.IsNotNull(typeof(RealRunMemoryImprovementStage));
        Assert.IsNotNull(typeof(RealRunMemoryImprovementSummary));
        Assert.IsNotNull(typeof(ManualRealRunMemoryImprovementValidator));

        var forbiddenStates = new[] { "Promoted", "Accepted", "Indexed", "Written", "Approved", "Applied" };
        var names = Enum.GetNames<ManualRealRunMemoryImprovementStatus>();
        foreach (var forbidden in forbiddenStates)
            Assert.IsFalse(names.Contains(forbidden, StringComparer.Ordinal), $"Status exposed forbidden memory-authority state: {forbidden}");
    }

    [TestMethod]
    public async Task ManualRealRunMemoryImprovement_RepeatedRealRunEvidenceProducesProposalOnlyCandidate()
    {
        var result = await new ManualRealRunMemoryImprovementService().RunAsync(BuildRequest());

        Assert.IsTrue(result.Succeeded, FormatIssues(result.Issues));
        Assert.AreEqual(ManualRealRunMemoryImprovementStatus.Succeeded, result.Status);
        Assert.IsNotNull(result.ImprovementStage);
        Assert.IsNotNull(result.Summary);
        Assert.IsNotNull(result.AuditEnvelope);

        var pattern = result.ImprovementStage.Patterns.Single();
        Assert.AreEqual("RepeatedFailureMode", pattern.PatternType);
        Assert.AreEqual(2, pattern.OccurrenceCount);
        Assert.IsTrue(pattern.RequiresHumanReview);
        Assert.IsFalse(pattern.CreatesAuthority);
        Assert.IsFalse(pattern.PromotesMemory);
        Assert.IsFalse(pattern.WritesCollectiveMemory);
        Assert.IsFalse(pattern.WritesWeaviate);

        var candidate = result.ImprovementStage.Candidates.Single();
        Assert.IsTrue(candidate.IsProposalOnly);
        Assert.IsTrue(candidate.RequiresHumanReview);
        Assert.IsFalse(candidate.CreatesAuthority);
        Assert.IsFalse(candidate.PromotesMemory);
        Assert.IsFalse(candidate.CreatesCollectiveMemory);
        Assert.IsFalse(candidate.WritesWeaviate);
        Assert.IsNotNull(candidate.ProposalDraft);
        Assert.IsTrue(candidate.ProposalDraft.IsProposalOnly);
        Assert.IsTrue(candidate.ProposalDraft.RequiresHumanReview);
        Assert.IsFalse(candidate.ProposalDraft.PromotesMemory);
        Assert.IsFalse(candidate.ProposalDraft.CreatesCollectiveMemory);
        Assert.IsTrue(candidate.ProposalDraft.EvidenceRefs.Count > 0);

        Assert.IsTrue(result.Summary.IsAdvisoryOnly);
        Assert.IsFalse(result.Summary.GrantsApproval);
        Assert.IsFalse(result.Summary.CreatesAuthority);
        Assert.IsFalse(result.Summary.PromotesMemory);
        Assert.IsFalse(result.Summary.CreatesCollectiveMemory);
        Assert.IsFalse(result.Summary.WritesWeaviate);
        AssertNoIssues(new Audit.AgentRunAuditEnvelopeValidator().Validate(result.AuditEnvelope));
        AssertNoIssues(new Audit.ThoughtLedgerSafetyValidator().Validate(result.AuditEnvelope.ThoughtLedger));
        Assert.IsTrue(result.AuditEnvelope.Outputs.Any(output => output.RefType == nameof(MemoryImprovementProposalDraft) && output.IsProposalOnly));
        AssertCapability(result.AuditEnvelope.CapabilityUses, AgentCapability.CreateMemoryProposal, Audit.AgentCapabilityUseOutcome.Allowed);
        AssertCapability(result.AuditEnvelope.CapabilityUses, AgentCapability.PromoteCollectiveMemory, Audit.AgentCapabilityUseOutcome.Blocked);
        AssertCapability(result.AuditEnvelope.CapabilityUses, AgentCapability.RunTool, Audit.AgentCapabilityUseOutcome.Blocked);
        AssertCapability(result.AuditEnvelope.CapabilityUses, AgentCapability.MutateSource, Audit.AgentCapabilityUseOutcome.Blocked);
        Assert.IsTrue(result.AuditEnvelope.BoundaryDecisions.All(decision => !decision.GrantsAuthority && !decision.GrantsHumanApproval && !decision.GrantsPolicyApproval && !decision.GrantsMemoryPromotion));
        Assert.IsTrue(result.AuditEnvelope.ThoughtLedger.All(entry => !entry.ContainsRawPrivateReasoning && !entry.GrantsAuthority && !entry.GrantsApproval && !entry.GrantsMemoryPromotion));
    }

    [TestMethod]
    public async Task ManualRealRunMemoryImprovement_NoPatternReturnsNoProposalNeededWithSafeAudit()
    {
        var result = await new ManualRealRunMemoryImprovementService().RunAsync(BuildRequest(evidenceItems:
        [
            BuildEvidence("evidence-1", "run-audit-1", "Single clean run evidence without repeated or explicit memory pattern.")
        ]));

        Assert.IsTrue(result.Succeeded, FormatIssues(result.Issues));
        Assert.AreEqual(ManualRealRunMemoryImprovementStatus.NoProposalNeeded, result.Status);
        Assert.IsNotNull(result.ImprovementStage);
        Assert.AreEqual(0, result.ImprovementStage.Patterns.Count);
        Assert.AreEqual(0, result.ImprovementStage.Candidates.Count);
        Assert.IsNotNull(result.Summary);
        Assert.IsTrue(result.Summary.IsAdvisoryOnly);
        Assert.IsNotNull(result.AuditEnvelope);
        AssertNoIssues(new Audit.AgentRunAuditEnvelopeValidator().Validate(result.AuditEnvelope));
        AssertNoIssues(new Audit.ThoughtLedgerSafetyValidator().Validate(result.AuditEnvelope.ThoughtLedger));
    }

    [TestMethod]
    public async Task ManualRealRunMemoryImprovement_RejectsMissingRequiredRequestFields()
    {
        var result = await new ManualRealRunMemoryImprovementService().RunAsync(BuildRequest() with
        {
            MemoryImprovementRunId = string.Empty,
            TenantId = string.Empty,
            ProjectId = string.Empty,
            RequestedByUserId = string.Empty
        });

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ManualRealRunMemoryImprovementStatus.InvalidRequest, result.Status);
        AssertHasIssue(result.Issues, ManualRealRunMemoryImprovementValidator.RealRunMemoryRequestRequired);
        AssertHasIssue(result.Issues, ManualRealRunMemoryImprovementValidator.RealRunMemoryScopeRequired);
        AssertHasIssue(result.Issues, ManualRealRunMemoryImprovementValidator.RealRunMemoryUserRequired);
        Assert.IsNull(result.ImprovementStage);
        Assert.IsNull(result.AuditEnvelope);
    }

    [TestMethod]
    public async Task ManualRealRunMemoryImprovement_RejectsUnsafeEvidenceShapes()
    {
        foreach (var request in new[]
        {
            BuildRequest() with { EvidenceBundle = BuildBundle([]) },
            BuildRequest(evidenceItems: [BuildEvidence("evidence-1", "run-audit-1", "Evidence") with { IsFromRealRun = false }]),
            BuildRequest(evidenceItems: [BuildEvidence("evidence-1", "run-audit-1", "Evidence") with { IsSanitised = false }]),
            BuildRequest(evidenceItems: [BuildEvidence("evidence-1", "run-audit-1", "Evidence") with { IsAuthoritativeForAction = true }]),
            BuildRequest(evidenceItems: [BuildEvidence("evidence-1", "run-audit-1", "RawPrompt: hidden.")]),
            BuildRequest(evidenceItems: [BuildEvidence("evidence-1", "run-audit-1", "Evidence") with { ContainsSecret = true }]),
            BuildRequest(evidenceItems: [BuildEvidence("evidence-1", "run-audit-1", "Evidence") with { ClaimsMemoryPromotion = true }]),
            BuildRequest() with { EvidenceBundle = BuildBundle([BuildEvidence("evidence-1", "run-audit-1", "Evidence")]) with { ContainsAuthorityClaim = true } },
            BuildRequest() with { EvidenceBundle = BuildBundle([BuildEvidence("evidence-1", "run-audit-1", "Evidence")]) with { ContainsMemoryPromotionClaim = true } }
        })
        {
            var result = await new ManualRealRunMemoryImprovementService().RunAsync(request);

            Assert.IsFalse(result.Succeeded);
            Assert.IsTrue(
                result.Status is ManualRealRunMemoryImprovementStatus.InvalidRequest or ManualRealRunMemoryImprovementStatus.RejectedUnsafeEvidence,
                $"Unexpected status {result.Status}");
            Assert.IsTrue(result.Issues.Count > 0);
            Assert.IsNull(result.AuditEnvelope);
        }
    }

    [TestMethod]
    public async Task ManualRealRunMemoryImprovement_ModelBackedAndPersistenceAreRejected()
    {
        var modelBacked = await new ManualRealRunMemoryImprovementService().RunAsync(BuildRequest() with { UseModelBackedDetector = true });
        var persisted = await new ManualRealRunMemoryImprovementService().RunAsync(BuildRequest() with { PersistProposal = true });

        Assert.IsFalse(modelBacked.Succeeded);
        Assert.AreEqual(ManualRealRunMemoryImprovementStatus.InvalidRequest, modelBacked.Status);
        AssertHasIssue(modelBacked.Issues, ManualRealRunMemoryImprovementValidator.RealRunMemoryModelBackedForbidden);
        Assert.IsNull(modelBacked.AuditEnvelope);

        Assert.IsFalse(persisted.Succeeded);
        Assert.AreEqual(ManualRealRunMemoryImprovementStatus.InvalidRequest, persisted.Status);
        AssertHasIssue(persisted.Issues, ManualRealRunMemoryImprovementValidator.RealRunMemoryPersistenceForbidden);
        Assert.IsNull(persisted.AuditEnvelope);
    }

    [TestMethod]
    public void ManualRealRunMemoryImprovementValidator_RejectsUnsafePatternsCandidatesAndProposalDrafts()
    {
        var validator = new ManualRealRunMemoryImprovementValidator();
        var pattern = BuildPattern();

        AssertHasIssue(validator.ValidatePattern(pattern with { CreatesAuthority = true }), ManualRealRunMemoryImprovementValidator.RealRunMemoryPatternInvalid);
        AssertHasIssue(validator.ValidatePattern(pattern with { PromotesMemory = true }), ManualRealRunMemoryImprovementValidator.RealRunMemoryPromotionForbidden);
        AssertHasIssue(validator.ValidatePattern(pattern with { WritesCollectiveMemory = true }), ManualRealRunMemoryImprovementValidator.RealRunMemoryPromotionForbidden);
        AssertHasIssue(validator.ValidatePattern(pattern with { WritesWeaviate = true }), ManualRealRunMemoryImprovementValidator.RealRunIndexWriteForbidden);

        var candidate = BuildCandidate(pattern);
        AssertHasIssue(validator.ValidateCandidate(candidate with { CreatesAuthority = true }), ManualRealRunMemoryImprovementValidator.RealRunMemoryCandidateInvalid);
        AssertHasIssue(validator.ValidateCandidate(candidate with { PromotesMemory = true }), ManualRealRunMemoryImprovementValidator.RealRunMemoryPromotionForbidden);
        AssertHasIssue(validator.ValidateCandidate(candidate with { CreatesCollectiveMemory = true }), ManualRealRunMemoryImprovementValidator.RealRunMemoryPromotionForbidden);
        AssertHasIssue(validator.ValidateCandidate(candidate with { WritesWeaviate = true }), ManualRealRunMemoryImprovementValidator.RealRunIndexWriteForbidden);
        AssertHasIssue(validator.ValidateCandidate(candidate with { ProposalDraft = candidate.ProposalDraft! with { EvidenceRefs = [] } }), ManualRealRunMemoryImprovementValidator.RealRunMemoryProposalUnsafe);
        AssertHasIssue(validator.ValidateCandidate(candidate with { ProposedSummary = "This is accepted memory now." }), ManualRealRunMemoryImprovementValidator.RealRunMemoryPromotionForbidden);
    }

    [TestMethod]
    public void ManualRealRunMemoryImprovement_ProductionFileDoesNotAddRuntimePersistenceIndexOrMutationBoundary()
    {
        var root = FindRepositoryRoot();
        var servicePath = Path.Combine(root, "IronDev.Core", "Agents", "Concrete", "ManualRealRunMemoryImprovementService.cs");
        var serviceText = File.ReadAllText(servicePath);
        var scanText = serviceText
            .Replace("AgentCapability.PromoteCollectiveMemory", string.Empty, StringComparison.Ordinal)
            .Replace("PromotesMemory", string.Empty, StringComparison.Ordinal)
            .Replace("WritesWeaviate", string.Empty, StringComparison.Ordinal)
            .Replace("CreatesCollectiveMemory", string.Empty, StringComparison.Ordinal);

        var forbiddenTokens = new[]
        {
            "SqlCollectiveMemoryPromotionService",
            "SqlMemoryImprovementProposalStore",
            "CollectiveMemoryPromotion",
            "AcceptedMemory",
            "WeaviateClient",
            "MemoryIndex",
            "AgentMemoryStore",
            "AppendMemory",
            "ProcessStartInfo",
            "System.Diagnostics.Process",
            "PowerShell",
            "cmd.exe",
            "bash",
            "git apply",
            "git commit",
            "git push",
            "File.WriteAllText",
            "File.Delete",
            "File.Copy",
            "Directory.Delete",
            "HttpClient",
            "OpenAiLlmService",
            "ChatCompletion",
            "ResponsesApi",
            "AddHostedService",
            "IHostedService",
            "BackgroundService",
            "AgentRuntime",
            "AgentScheduler",
            "AgentOrchestrator",
            "AgentToolRouter",
            "SubmitReview",
            "CreatePullRequest"
        };

        foreach (var token in forbiddenTokens)
        {
            Assert.IsFalse(scanText.Contains(token, StringComparison.Ordinal),
                $"Manual real-run memory improvement service introduced forbidden token '{token}'.");
        }
    }

    [TestMethod]
    public void ManualRealRunMemoryImprovement_IsNotRuntimeApiCliStoreOrLoopWired()
    {
        var root = FindRepositoryRoot();
        var runtimeRoots = new[] { "IronDev.Infrastructure", "IronDev.Api", "IronDev.Client", "tools" };
        var runtimeFiles = runtimeRoots
            .Select(path => Path.Combine(root, path))
            .Where(Directory.Exists)
            .SelectMany(path => Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Contains(Path.Combine("bin"), StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains(Path.Combine("obj"), StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (var file in runtimeFiles)
        {
            var text = File.ReadAllText(file);
            Assert.IsFalse(text.Contains(nameof(ManualRealRunMemoryImprovementService), StringComparison.Ordinal),
                $"Runtime/API/CLI file wires ManualRealRunMemoryImprovementService: {file}");
            Assert.IsFalse(text.Contains(nameof(IManualRealRunMemoryImprovementService), StringComparison.Ordinal),
                $"Runtime/API/CLI file wires IManualRealRunMemoryImprovementService: {file}");
        }

        foreach (var loopFile in new[]
        {
            Path.Combine(root, "IronDev.Core", "Agents", "Concrete", "ManualTicketReviewFixProposalLoopService.cs"),
            Path.Combine(root, "IronDev.Core", "Agents", "Concrete", "ManualTestFailureRepairProposalLoopService.cs")
        })
        {
            var text = File.ReadAllText(loopFile);
            Assert.IsFalse(text.Contains(nameof(ManualRealRunMemoryImprovementService), StringComparison.Ordinal),
                $"Manual loop auto-calls ManualRealRunMemoryImprovementService: {loopFile}");
        }
    }

    private static ManualRealRunMemoryImprovementRequest BuildRequest(IReadOnlyList<RealRunEvidenceItem>? evidenceItems = null) =>
        new()
        {
            MemoryImprovementRunId = "real-run-memory-001",
            TenantId = "tenant-1",
            ProjectId = "project-1",
            CampaignId = "campaign-1",
            RequestedByUserId = "human-reviewer",
            RequestedAtUtc = RequestedAt,
            EvidenceBundle = BuildBundle(evidenceItems ??
            [
                BuildEvidence("evidence-1", "run-audit-1", "Tester failure needed the same missing CLI envelope convention."),
                BuildEvidence("evidence-2", "tool-audit-1", "Tester failure needed the same missing CLI envelope convention.")
            ])
        };

    private static RealRunEvidenceBundle BuildBundle(IReadOnlyList<RealRunEvidenceItem> items) =>
        new() { Items = items };

    private static RealRunEvidenceItem BuildEvidence(string evidenceId, string refId, string summary) =>
        new()
        {
            EvidenceId = evidenceId,
            RefType = "AgentRunAuditEnvelope",
            RefId = refId,
            Source = "manual dogfood run",
            Summary = summary,
            EvidenceRefs = [$"run:{refId}", $"evidence:{evidenceId}"],
            SupportsMemoryImprovement = true,
            IsFromRealRun = true,
            IsSanitised = true
        };

    private static RealRunMemoryPattern BuildPattern() =>
        new()
        {
            PatternId = "pattern-1",
            PatternType = "RepeatedFailureMode",
            Summary = "Repeated test failure mode.",
            EvidenceRefs = ["evidence-1"],
            OccurrenceCount = 2,
            IsActionable = true,
            RequiresHumanReview = true
        };

    private static RealRunMemoryImprovementCandidate BuildCandidate(RealRunMemoryPattern pattern)
    {
        var sourcePattern = new MemoryImprovementPatternFinding
        {
            PatternFindingId = "finding-1",
            PatternType = MemoryImprovementPatternType.RepeatedFailureMode,
            Summary = pattern.Summary,
            Confidence = 0.8m,
            EvidenceRefs = pattern.EvidenceRefs,
            RequiresHumanReview = true
        };

        var proposal = new MemoryImprovementProposalDraft
        {
            ProposalDraftId = "proposal-1",
            Title = "Review repeated failure evidence",
            Summary = "Draft a proposal-only memory improvement for human review.",
            Rationale = "Evidence repeats across governed runs.",
            SourcePattern = sourcePattern,
            EvidenceRefs = pattern.EvidenceRefs,
            IsProposalOnly = true,
            RequiresHumanReview = true
        };

        return new RealRunMemoryImprovementCandidate
        {
            CandidateId = "candidate-1",
            CandidateType = pattern.PatternType,
            ProposedTitle = proposal.Title,
            ProposedSummary = proposal.Summary,
            Patterns = [pattern],
            EvidenceRefs = pattern.EvidenceRefs,
            ProposalDraft = proposal,
            IsProposalOnly = true,
            RequiresHumanReview = true
        };
    }

    private static void AssertCapability(
        IReadOnlyList<Audit.AgentCapabilityUseRecord> uses,
        AgentCapability capability,
        Audit.AgentCapabilityUseOutcome outcome)
    {
        var use = uses.SingleOrDefault(item => item.Capability == capability);
        Assert.IsNotNull(use, $"Missing capability use for {capability}.");
        Assert.AreEqual(outcome, use.Outcome);
    }

    private static void AssertHasIssue(IReadOnlyList<ManualRealRunMemoryImprovementIssue> issues, string code)
    {
        Assert.IsTrue(
            issues.Any(issue => string.Equals(issue.Code, code, StringComparison.Ordinal)),
            $"Expected issue '{code}' but got: {string.Join(", ", issues.Select(issue => issue.Code))}");
    }

    private static void AssertNoIssues(IReadOnlyList<AgentDefinitionValidationIssue> issues)
    {
        Assert.AreEqual(
            0,
            issues.Count,
            $"Expected no issues but got: {string.Join(Environment.NewLine, issues.Select(issue => $"{issue.Code}: {issue.Message}"))}");
    }

    private static string FormatIssues(IReadOnlyList<ManualRealRunMemoryImprovementIssue> issues) =>
        string.Join(Environment.NewLine, issues.Select(issue => $"{issue.Code}: {issue.Message}"));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
