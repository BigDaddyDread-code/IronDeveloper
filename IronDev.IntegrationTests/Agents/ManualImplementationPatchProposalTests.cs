using IronDev.Core.Agents;
using IronDev.Core.Agents.Audit;
using IronDev.Core.Agents.Concrete;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using AuditThoughtLedgerEntry = IronDev.Core.Agents.Audit.ThoughtLedgerEntry;

namespace IronDev.IntegrationTests.Agents;

[TestClass]
public sealed class ManualImplementationPatchProposalTests
{
        [TestMethod]
    public void ManualImplementationPatchProposalContracts_ExposeExpectedTypes()
    {
        Assert.IsNotNull(typeof(IManualImplementationAgentPatchProposalService));
        Assert.IsNotNull(typeof(ManualImplementationAgentPatchProposalService));
        Assert.IsNotNull(typeof(ManualImplementationPatchProposalRequest));
        Assert.IsNotNull(typeof(ManualImplementationPatchProposalResult));
        Assert.IsNotNull(typeof(ManualImplementationPatchProposalStatus));
        Assert.IsNotNull(typeof(ManualImplementationPatchProposalIssue));
        Assert.IsNotNull(typeof(ManualImplementationPatchProposalOutput));
        Assert.IsNotNull(typeof(ManualImplementationPatchProposalValidator));
        Assert.IsNotNull(typeof(IPatchProposalGenerator));
        Assert.IsNotNull(typeof(ScriptedPatchProposalGenerator));
        Assert.IsNotNull(typeof(PatchProposalGenerationRequest));
        Assert.IsNotNull(typeof(PatchProposalGenerationResult));
        Assert.IsNotNull(typeof(PatchProposalPackage));
        Assert.IsNotNull(typeof(ProposedFileChange));
        Assert.IsNotNull(typeof(ProposedPatchHunk));
        Assert.IsNotNull(typeof(PatchProposalInputRef));
    }

    [TestMethod]
    public void ManualImplementationPatchProposal_ValidGatedPatchProposalInvokesGeneratorAndReturnsSafeAudit()
    {
        var generator = BackendManualToolExecutionFixtures.ScriptedPatchProposalGeneratorReturnsProposalOnlyPackage();
        var service = new ManualImplementationAgentPatchProposalService(generator);
        var request = BackendManualToolExecutionFixtures.PatchProposalRequestThatDoesNotApplySource();

        var result = service.Propose(request);

        Assert.AreEqual(1, generator.CallCount);
        Assert.IsTrue(result.Succeeded, FormatIssues(result.Issues));
        Assert.AreEqual(ManualImplementationPatchProposalStatus.Succeeded, result.Status);
        Assert.IsNotNull(result.Output);
        Assert.IsNotNull(result.AuditEnvelope);
        Assert.AreEqual(request.ToolRequest.ToolRequestId, result.ToolRequestId);
        Assert.AreEqual(request.GateDecision.GateDecisionId, result.GateDecisionId);
        AssertOutputHasNoDangerousFlags(result.Output);
        AssertPackageIsProposalOnly(result.Output.Proposal);
        AssertNoAuditIssues(result.AuditEnvelope);
        AssertNoThoughtLedgerIssues(result.AuditEnvelope.ThoughtLedger);
        AssertCapability(result.AuditEnvelope, AgentCapability.CreateReport, AgentCapabilityUseOutcome.Allowed);
        AssertCapability(result.AuditEnvelope, AgentCapability.RunTool, AgentCapabilityUseOutcome.Blocked);
        AssertCapability(result.AuditEnvelope, AgentCapability.MutateSource, AgentCapabilityUseOutcome.Blocked);
        AssertCapability(result.AuditEnvelope, AgentCapability.CallExternalSystem, AgentCapabilityUseOutcome.Blocked);
        AssertCapability(result.AuditEnvelope, AgentCapability.PromoteCollectiveMemory, AgentCapabilityUseOutcome.Blocked);
        Assert.IsFalse(result.AuditEnvelope.CapabilityUses.Any(use => use.Capability == AgentCapability.RunTool && use.Outcome == AgentCapabilityUseOutcome.Allowed));
        Assert.IsTrue(result.AuditEnvelope.BoundaryDecisions.All(decision => !decision.GrantsAuthority && !decision.GrantsHumanApproval && !decision.GrantsPolicyApproval && !decision.GrantsMemoryPromotion));
        Assert.IsTrue(result.AuditEnvelope.BoundaryDecisions.Any(decision => decision.BoundaryDecisionId.EndsWith("source-mutation-blocked", StringComparison.Ordinal)));
        Assert.IsTrue(result.AuditEnvelope.BoundaryDecisions.Any(decision => decision.BoundaryDecisionId.EndsWith("patch-apply-blocked", StringComparison.Ordinal)));
        Assert.IsTrue(result.AuditEnvelope.BoundaryDecisions.Any(decision => decision.BoundaryDecisionId.EndsWith("file-write-blocked", StringComparison.Ordinal)));
        Assert.IsTrue(result.AuditEnvelope.BoundaryDecisions.Any(decision => decision.BoundaryDecisionId.EndsWith("git-command-blocked", StringComparison.Ordinal)));
        Assert.IsTrue(result.AuditEnvelope.ThoughtLedger.All(entry => !entry.ContainsRawPrivateReasoning && !entry.GrantsAuthority && !entry.GrantsApproval && !entry.GrantsMemoryPromotion));
        Assert.IsFalse(result.AuditEnvelope.ThoughtLedger.Any(entry => entry.Summary.Contains("patch applied", StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(result.AuditEnvelope.ThoughtLedger.Any(entry => entry.Summary.Contains("file written", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void ManualImplementationPatchProposal_GeneratorFailureReturnsFailedWithoutAudit()
    {
        var generator = new ScriptedPatchProposalGenerator(_ => new PatchProposalGenerationResult
        {
            Succeeded = false,
            Summary = "Generator could not produce a safe proposal.",
            Issues =
            [
                new ManualImplementationPatchProposalIssue
                {
                    Code = ManualImplementationPatchProposalValidator.OutputUnsafe,
                    Severity = AgentDefinitionValidator.SeverityError,
                    Message = "No safe proposal.",
                    Field = "Generator"
                }
            ]
        });
        var service = new ManualImplementationAgentPatchProposalService(generator);

        var result = service.Propose(BackendManualToolExecutionFixtures.PatchProposalRequestThatDoesNotApplySource());

        Assert.AreEqual(1, generator.CallCount);
        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ManualImplementationPatchProposalStatus.Failed, result.Status);
        Assert.IsNull(result.Output);
        Assert.IsNull(result.AuditEnvelope);
        AssertHasIssue(result.Issues, ManualImplementationPatchProposalValidator.OutputUnsafe);
    }

    [TestMethod]
    public void ManualImplementationPatchProposal_GateRejectionsPreventGeneratorCall()
    {
        var baseRequest = BackendManualToolExecutionFixtures.PatchProposalRequestThatDoesNotApplySource();
        var unsafeGateCases = new[]
        {
            baseRequest.GateDecision with { Decision = AgentToolExecutionGateDecisionType.Blocked, GrantsExecution = false, RequiresExecutor = false },
            baseRequest.GateDecision with { Decision = AgentToolExecutionGateDecisionType.RequiresApproval, GrantsExecution = false, RequiresExecutor = false },
            baseRequest.GateDecision with { GrantsExecution = false },
            baseRequest.GateDecision with { RequiresExecutor = false },
            baseRequest.GateDecision with { ExecutesTool = true },
            baseRequest.GateDecision with { MutatesSource = true },
            baseRequest.GateDecision with { CallsExternalSystem = true },
            baseRequest.GateDecision with { SubmitsGitHubReview = true },
            baseRequest.GateDecision with { PersistsResult = true },
            baseRequest.GateDecision with { PromotesMemory = true },
            baseRequest.GateDecision with { CreatesCollectiveMemory = true },
            baseRequest.GateDecision with { WritesWeaviate = true },
            baseRequest.GateDecision with { ToolRequestId = "different-tool-request" }
        };

        foreach (var gate in unsafeGateCases)
        {
            var generator = BackendManualToolExecutionFixtures.ScriptedPatchProposalGeneratorReturnsProposalOnlyPackage();
            var service = new ManualImplementationAgentPatchProposalService(generator);
            var result = service.Propose(baseRequest with { GateDecision = gate });

            Assert.AreEqual(0, generator.CallCount);
            Assert.IsFalse(result.Succeeded);
            Assert.IsTrue(result.Status is ManualImplementationPatchProposalStatus.Blocked or ManualImplementationPatchProposalStatus.InvalidRequest);
            Assert.IsNull(result.Output);
            Assert.IsNull(result.AuditEnvelope);
            Assert.IsTrue(result.Issues.Count > 0);
        }
    }

    [TestMethod]
    public void ManualImplementationPatchProposal_RequestRejectionsPreventGeneratorCall()
    {
        var baseRequest = BackendManualToolExecutionFixtures.PatchProposalRequestThatDoesNotApplySource();
        var cases = new[]
        {
            baseRequest with { ManualProposalId = string.Empty },
            baseRequest with { RequestedByUserId = string.Empty },
            baseRequest with { ProposalGoal = string.Empty },
            baseRequest with { Inputs = [] },
            baseRequest with { ToolRequest = baseRequest.ToolRequest with { ToolKind = AgentToolKind.TestRun } },
            baseRequest with { ToolRequest = baseRequest.ToolRequest with { RequestType = AgentToolRequestType.TestExecutionRequest } },
            baseRequest with { ToolRequest = baseRequest.ToolRequest with { RiskLevel = AgentToolRiskLevel.High } },
            baseRequest with { ToolRequest = baseRequest.ToolRequest with { Actor = BackendAgentToolRequestFixtures.ActorFor(AgentDefinitionCatalog.TestingAgent) } },
            baseRequest with { ToolRequest = baseRequest.ToolRequest with { ClaimsApproval = true } },
            baseRequest with { ToolRequest = baseRequest.ToolRequest with { ClaimsExecutionPermission = true } },
            baseRequest with { ToolRequest = baseRequest.ToolRequest with { ContainsExecutionResult = true } },
            baseRequest with { ToolRequest = baseRequest.ToolRequest with { IsExecutableWithoutGate = true } },
            baseRequest with { Inputs = [BackendManualToolExecutionFixtures.SanitisedIssuePatchProposalInput() with { IsAuthoritativeForAction = true }] },
            baseRequest with { Inputs = [BackendManualToolExecutionFixtures.SanitisedIssuePatchProposalInput() with { ContainsRawPrivateReasoning = true }] },
            baseRequest with { Inputs = [BackendManualToolExecutionFixtures.SanitisedIssuePatchProposalInput() with { ContainsSecret = true, IsSanitised = false }] },
            baseRequest with { Parameters = new Dictionary<string, string> { ["filter"] = "A && B" } },
            baseRequest with { Parameters = new Dictionary<string, string> { ["password"] = "not-allowed" } }
        };

        foreach (var request in cases)
        {
            var generator = BackendManualToolExecutionFixtures.ScriptedPatchProposalGeneratorReturnsProposalOnlyPackage();
            var result = new ManualImplementationAgentPatchProposalService(generator).Propose(request);

            Assert.AreEqual(0, generator.CallCount);
            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(ManualImplementationPatchProposalStatus.InvalidRequest, result.Status);
            Assert.IsNull(result.Output);
            Assert.IsNull(result.AuditEnvelope);
            Assert.IsTrue(result.Issues.Count > 0);
        }
    }

    [TestMethod]
    public void ManualImplementationPatchProposal_UnsafeGeneratorOutputIsRejectedWithoutAudit()
    {
        var cases = new[]
        {
            BackendManualToolExecutionFixtures.PatchProposalPackageThatDoesNotApply() with { AppliesPatch = true },
            BackendManualToolExecutionFixtures.PatchProposalPackageThatDoesNotApply() with { MutatesSource = true },
            BackendManualToolExecutionFixtures.PatchProposalPackageThatDoesNotApply() with { CreatesAuthority = true },
            BackendManualToolExecutionFixtures.PatchProposalPackageThatDoesNotApply() with { AppliesCleanlyClaimed = true },
            BackendManualToolExecutionFixtures.PatchProposalPackageThatDoesNotApply() with { EvidenceRefs = [] },
            BackendManualToolExecutionFixtures.PatchProposalPackageThatDoesNotApply() with { FileChanges = [] },
            BackendManualToolExecutionFixtures.PatchProposalPackageThatDoesNotApply() with { FileChanges = [BackendManualToolExecutionFixtures.ProposedFileChangeThatDoesNotWrite() with { Path = "..\\outside.cs" }] },
            BackendManualToolExecutionFixtures.PatchProposalPackageThatDoesNotApply() with { FileChanges = [BackendManualToolExecutionFixtures.ProposedFileChangeThatDoesNotWrite() with { WritesFile = true }] },
            BackendManualToolExecutionFixtures.PatchProposalPackageThatDoesNotApply() with { FileChanges = [BackendManualToolExecutionFixtures.ProposedFileChangeThatDoesNotWrite() with { DeletesFile = true }] },
            BackendManualToolExecutionFixtures.PatchProposalPackageThatDoesNotApply() with { FileChanges = [BackendManualToolExecutionFixtures.ProposedFileChangeThatDoesNotWrite() with { AppliesPatch = true }] },
            BackendManualToolExecutionFixtures.PatchProposalPackageThatDoesNotApply() with { FileChanges = [BackendManualToolExecutionFixtures.ProposedFileChangeThatDoesNotWrite() with { Hunks = [] }] },
            BackendManualToolExecutionFixtures.PatchProposalPackageThatDoesNotApply() with { FileChanges = [BackendManualToolExecutionFixtures.ProposedFileChangeThatDoesNotWrite() with { Hunks = [BackendManualToolExecutionFixtures.ProposedPatchHunkThatIsNotApplied() with { ClaimsApplied = true }] }] },
            BackendManualToolExecutionFixtures.PatchProposalPackageThatDoesNotApply() with { FileChanges = [BackendManualToolExecutionFixtures.ProposedFileChangeThatDoesNotWrite() with { Hunks = [BackendManualToolExecutionFixtures.ProposedPatchHunkThatIsNotApplied() with { ContainsSecret = true }] }] },
            BackendManualToolExecutionFixtures.PatchProposalPackageThatDoesNotApply() with { FileChanges = [BackendManualToolExecutionFixtures.ProposedFileChangeThatDoesNotWrite() with { Hunks = [BackendManualToolExecutionFixtures.ProposedPatchHunkThatIsNotApplied() with { EvidenceRefs = [] }] }] }
        };

        foreach (var package in cases)
        {
            var generator = new ScriptedPatchProposalGenerator(_ => new PatchProposalGenerationResult
            {
                Succeeded = true,
                Summary = "Unsafe proposal package.",
                Proposal = package,
                EvidenceRefs = ["evidence-package-1"]
            });

            var result = new ManualImplementationAgentPatchProposalService(generator).Propose(BackendManualToolExecutionFixtures.PatchProposalRequestThatDoesNotApplySource());

            Assert.AreEqual(1, generator.CallCount);
            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(ManualImplementationPatchProposalStatus.InvalidRequest, result.Status);
            Assert.IsNull(result.AuditEnvelope);
            Assert.IsTrue(result.Issues.Any(issue => issue.Code is ManualImplementationPatchProposalValidator.PackageInvalid or ManualImplementationPatchProposalValidator.FileChangeInvalid or ManualImplementationPatchProposalValidator.HunkInvalid));
        }
    }

    [TestMethod]
    public void ManualImplementationPatchProposal_OutputValidatorRejectsDangerousFlags()
    {
        var validator = new ManualImplementationPatchProposalValidator();
        var cases = new[]
        {
            BackendManualToolExecutionFixtures.PatchProposalOutputThatDoesNotApplySource() with { ContainsRawPrivateReasoning = true },
            BackendManualToolExecutionFixtures.PatchProposalOutputThatDoesNotApplySource() with { MutatesSource = true },
            BackendManualToolExecutionFixtures.PatchProposalOutputThatDoesNotApplySource() with { AppliesPatch = true },
            BackendManualToolExecutionFixtures.PatchProposalOutputThatDoesNotApplySource() with { WritesFiles = true },
            BackendManualToolExecutionFixtures.PatchProposalOutputThatDoesNotApplySource() with { DeletesFiles = true },
            BackendManualToolExecutionFixtures.PatchProposalOutputThatDoesNotApplySource() with { RunsGit = true },
            BackendManualToolExecutionFixtures.PatchProposalOutputThatDoesNotApplySource() with { CallsExternalSystem = true },
            BackendManualToolExecutionFixtures.PatchProposalOutputThatDoesNotApplySource() with { SubmitsGitHubReview = true },
            BackendManualToolExecutionFixtures.PatchProposalOutputThatDoesNotApplySource() with { CreatesPullRequest = true },
            BackendManualToolExecutionFixtures.PatchProposalOutputThatDoesNotApplySource() with { PromotesMemory = true },
            BackendManualToolExecutionFixtures.PatchProposalOutputThatDoesNotApplySource() with { CreatesCollectiveMemory = true },
            BackendManualToolExecutionFixtures.PatchProposalOutputThatDoesNotApplySource() with { WritesWeaviate = true },
            BackendManualToolExecutionFixtures.PatchProposalOutputThatDoesNotApplySource() with { EvidenceRefs = [] }
        };

        foreach (var output in cases)
            AssertHasIssue(validator.ValidateOutput(output), ManualImplementationPatchProposalValidator.OutputUnsafe);
    }

    [TestMethod]
    public void ManualImplementationPatchProposal_ImplementationAgentDoesNotNeedSourceMutationPermission()
    {
        var request = BackendManualToolExecutionFixtures.PatchProposalRequestThatDoesNotApplySource();

        Assert.IsFalse(request.GateDecision.MutatesSource);
        Assert.IsFalse(request.GateDecision.ExecutesTool);
        Assert.IsFalse(request.GateDecision.CallsExternalSystem);
        Assert.IsFalse(request.GateDecision.PersistsResult);
        Assert.IsFalse(request.GateDecision.PromotesMemory);
        Assert.IsFalse(request.GateDecision.WritesWeaviate);
        Assert.IsTrue(request.ToolRequest.Actor.DeclaredCapabilities.Contains(AgentCapability.CreateReport));

        var result = new ManualImplementationAgentPatchProposalService(BackendManualToolExecutionFixtures.ScriptedPatchProposalGeneratorReturnsProposalOnlyPackage()).Propose(request);

        Assert.IsTrue(result.Succeeded, FormatIssues(result.Issues));
        AssertCapability(result.AuditEnvelope!, AgentCapability.CreateReport, AgentCapabilityUseOutcome.Allowed);
        AssertCapability(result.AuditEnvelope!, AgentCapability.MutateSource, AgentCapabilityUseOutcome.Blocked);
    }

    [TestMethod]
    public void ManualImplementationPatchProposal_NoRuntimeApiPersistenceModelOrTesterWiring()
    {
        var files = new[]
        {
            ReadRepositoryFile("IronDev.Api", "Program.cs"),
            ReadRepositoryFile("IronDev.Core", "Agents", "Concrete", "ManualIndependentCriticAgentService.cs"),
            ReadRepositoryFile("IronDev.Core", "Agents", "Concrete", "ManualMemoryImprovementAgentService.cs"),
            ReadRepositoryFile("IronDev.Core", "Agents", "Concrete", "ModelBackedManualIndependentCriticAgentService.cs"),
            ReadRepositoryFile("IronDev.Core", "Agents", "Concrete", "ModelBackedManualMemoryImprovementAgentService.cs"),
            ReadRepositoryFile("IronDev.Core", "Agents", "Concrete", "StoredManualAgentExecutionService.cs"),
            ReadRepositoryFile("IronDev.Core", "Agents", "Concrete", "ManualTesterAgentToolExecutionService.cs")
        };
        var forbidden = new[]
        {
            "ManualImplementationAgentPatchProposalService",
            "IManualImplementationAgentPatchProposalService",
            "AgentToolRouter",
            "IAgentToolRouter",
            "ToolExecutionAuditStore",
            "SqlToolExecutionAuditStore"
        };

        foreach (var file in files)
        {
            foreach (var token in forbidden)
                Assert.IsFalse(file.Contains(token, StringComparison.Ordinal), $"Unexpected runtime/API/persistence wiring token found: {token}");
        }
    }

    [TestMethod]
    public void ManualImplementationPatchProposal_StaticBoundary_HasNoRuntimePersistenceExternalShellOrMutationTokens()
    {
        var changedProductionFiles = new[]
        {
            ReadRepositoryFile("IronDev.Core", "Agents", "Concrete", "ManualImplementationAgentPatchProposalService.cs")
        };
        var forbidden = new[]
        {
            "ProcessStartInfo",
            "System.Diagnostics.Process",
            "PowerShell",
            "cmd.exe",
            "bash",
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
            "SqlCollectiveMemoryPromotionService",
            "git apply",
            "git commit",
            "git push"
        };

        foreach (var source in changedProductionFiles)
        {
            foreach (var token in forbidden)
                Assert.IsFalse(source.Contains(token, StringComparison.Ordinal), $"Forbidden active token found in manual implementation proposal production file: {token}");
        }
    }

    private static void AssertOutputHasNoDangerousFlags(ManualImplementationPatchProposalOutput output)
    {
        Assert.IsFalse(output.ContainsRawPrivateReasoning);
        Assert.IsFalse(output.MutatesSource);
        Assert.IsFalse(output.AppliesPatch);
        Assert.IsFalse(output.WritesFiles);
        Assert.IsFalse(output.DeletesFiles);
        Assert.IsFalse(output.RunsGit);
        Assert.IsFalse(output.CallsExternalSystem);
        Assert.IsFalse(output.SubmitsGitHubReview);
        Assert.IsFalse(output.CreatesPullRequest);
        Assert.IsFalse(output.PromotesMemory);
        Assert.IsFalse(output.CreatesCollectiveMemory);
        Assert.IsFalse(output.WritesWeaviate);
    }

    private static void AssertPackageIsProposalOnly(PatchProposalPackage package)
    {
        Assert.IsTrue(package.IsProposalOnly);
        Assert.IsTrue(package.RequiresHumanReview);
        Assert.IsTrue(package.RequiresValidation);
        Assert.IsFalse(package.AppliesCleanlyClaimed);
        Assert.IsFalse(package.CreatesAuthority);
        Assert.IsFalse(package.CreatesRuntimeAction);
        Assert.IsFalse(package.MutatesSource);
        Assert.IsFalse(package.AppliesPatch);
        Assert.IsTrue(package.FileChanges.Count > 0);
        Assert.IsTrue(package.EvidenceRefs.Count > 0);

        foreach (var change in package.FileChanges)
        {
            Assert.IsTrue(change.IsProposalOnly);
            Assert.IsFalse(change.WritesFile);
            Assert.IsFalse(change.DeletesFile);
            Assert.IsFalse(change.AppliesPatch);
            Assert.IsTrue(change.EvidenceRefs.Count > 0);
            Assert.IsTrue(change.Hunks.Count > 0);

            foreach (var hunk in change.Hunks)
            {
                Assert.IsFalse(hunk.ContainsRawPrivateReasoning);
                Assert.IsFalse(hunk.ContainsSecret);
                Assert.IsFalse(hunk.ClaimsApplied);
                Assert.IsTrue(hunk.EvidenceRefs.Count > 0);
            }
        }
    }

    private static void AssertCapability(
        AgentRunAuditEnvelope envelope,
        AgentCapability capability,
        AgentCapabilityUseOutcome outcome) =>
        Assert.IsTrue(
            envelope.CapabilityUses.Any(use => use.Capability == capability && use.Outcome == outcome),
            $"Expected {capability} to be {outcome}.");

    private static void AssertNoAuditIssues(AgentRunAuditEnvelope envelope)
    {
        var issues = new AgentRunAuditEnvelopeValidator().Validate(envelope);
        Assert.IsFalse(issues.Any(), FormatDefinitionIssues(issues));
    }

    private static void AssertNoThoughtLedgerIssues(IReadOnlyList<AuditThoughtLedgerEntry> entries)
    {
        var issues = new ThoughtLedgerSafetyValidator().Validate(entries);
        Assert.IsFalse(issues.Any(), FormatDefinitionIssues(issues));
    }

    private static void AssertHasIssue(IReadOnlyList<ManualImplementationPatchProposalIssue> issues, string code) =>
        Assert.IsTrue(issues.Any(issue => issue.Code == code), $"Expected issue {code}.{Environment.NewLine}{FormatIssues(issues)}");

    private static string FormatIssues(IReadOnlyList<ManualImplementationPatchProposalIssue> issues) =>
        string.Join(Environment.NewLine, issues.Select(issue => $"{issue.Code}: {issue.Message}"));

    private static string FormatDefinitionIssues(IReadOnlyList<AgentDefinitionValidationIssue> issues) =>
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
