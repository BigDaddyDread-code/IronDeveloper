using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Cli;
using IronDev.Core.Governance;
using IronDev.Core.Memory;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockAKMemoryInformedPlanningTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [TestMethod]
    public void BlockAK_AcceptedMemoryRetriever_ReadsAcceptedMemoryOnlyAndPreservesScope()
    {
        var root = CreateTempPath("ak-memory");
        var run = CreateTempPath("ak-run");
        try
        {
            var store = new AcceptedMemoryStore(root);
            var project = AppendAcceptedMemory(store, MemoryScope.Project, "project-alpha", "Prefer focused regression tests for boundary fixes.");
            var portable = AppendAcceptedMemory(store, MemoryScope.PortableEngineering, "project-alpha", "Prefer evidence-first reviews for risky changes.");
            _ = AppendAcceptedMemory(store, MemoryScope.Project, "project-other", "Other project convention must not leak.");
            _ = AppendAcceptedMemory(store, MemoryScope.PortableEngineering, "project-alpha", "IronDeveloper PR #448 src/Foo.cs project detail should not be portable.");
            Directory.CreateDirectory(run);
            File.WriteAllText(Path.Combine(run, "memory-proposals.jsonl"), "{\"memoryProposalId\":\"staged-only\"}");

            var result = AcceptedMemoryRetriever.Retrieve(store, Request("run-ak", "project-alpha"), DateTimeOffset.UtcNow);

            Assert.AreEqual(2, result.Items.Length);
            Assert.IsTrue(result.Items.Any(item => item.MemoryId == project.Record.MemoryId && item.MemoryScope == MemoryScope.Project));
            Assert.IsTrue(result.Items.Any(item => item.MemoryId == portable.Record.MemoryId && item.MemoryScope == MemoryScope.PortableEngineering));
            Assert.IsFalse(result.Items.Any(item => item.SafeContent.Contains("Other project", StringComparison.OrdinalIgnoreCase)));
            Assert.IsFalse(result.Items.Any(item => item.SafeContent.Contains("staged-only", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(result.Warnings.Any(item => item.StartsWith("PortableMemoryProjectSpecificDetailExcluded", StringComparison.OrdinalIgnoreCase)));
            AssertBoundary(result.Boundary);
        }
        finally
        {
            TryDelete(root);
            TryDelete(run);
        }
    }

    [TestMethod]
    public void BlockAK_MemoryCitationBundle_IsRequiredBeforePlannerContextUsesMemory()
    {
        var root = CreateTempPath("ak-memory");
        try
        {
            var store = new AcceptedMemoryStore(root);
            var first = AppendAcceptedMemory(store, MemoryScope.Project, "project-alpha", "Use narrow scoped patches.");
            _ = AppendAcceptedMemory(store, MemoryScope.Project, "project-alpha", "Watch stale source apply evidence.");
            var retrieval = AcceptedMemoryRetriever.Retrieve(store, Request("run-ak-citations", "project-alpha"));
            var allCitations = MemoryCitationWriter.CreateBundle(retrieval);
            var partialBundle = allCitations with
            {
                Citations = allCitations.Citations.Where(item => item.MemoryId == first.Record.MemoryId).ToArray()
            };

            var context = PlannerContextBuilder.Build(ContextRequest("run-ak-citations"), retrieval, partialBundle);

            Assert.AreEqual(1, context.AcceptedMemoryRefs.Length);
            Assert.AreEqual(partialBundle.MemoryCitationBundleId, context.MemoryCitationBundleId);
            Assert.IsFalse(context.KnownConstraints.Any(item => item.Contains("authority", StringComparison.OrdinalIgnoreCase)));
            AssertBoundary(context.Boundary);
            foreach (var citation in partialBundle.Citations)
            {
                Assert.IsFalse(MemoryCitationWriter.IsAuthorityUse(citation.UsedFor));
                Assert.IsFalse(citation.Caveat.Contains("approval", StringComparison.OrdinalIgnoreCase));
            }
        }
        finally
        {
            TryDelete(root);
        }
    }

    [TestMethod]
    public void BlockAK_MemoryInformedPlanProposal_IsMapNotMotion()
    {
        var context = new PlannerContextBundle
        {
            PlannerContextBundleId = "planner-context-1",
            RunId = "run-ak-plan",
            TaskSummary = "Add a small boundary fix.",
            RepoIdentity = "repo",
            BaseCommit = "abc123",
            RelevantFiles = ["IronDev.Core/Foo.cs"],
            AcceptedMemoryRefs = ["mem-cite-1"],
            MemoryCitationBundleId = "bundle-1",
            KnownRisks = ["Boundary fixes need focused tests."],
            KnownConstraints = ["Project memory is scoped to this project only."],
            SuggestedTestHints = ["Run focused boundary tests."],
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Boundary = MemoryPlanningBoundary.ContextEvidence
        };
        var bundle = new MemoryCitationBundle
        {
            MemoryCitationBundleId = "bundle-1",
            RunId = "run-ak-plan",
            RetrievalResultId = "retrieval-1",
            Citations =
            [
                new MemoryCitation
                {
                    CitationId = "mem-cite-1",
                    MemoryId = "mem-1",
                    MemoryVersionId = "mem-ver-1",
                    MemoryScope = MemoryScope.Project,
                    EvidenceRefs = ["evidence-1"],
                    UsedFor = MemoryCitationUsedFor.PlanContext,
                    Caveat = "Project memory is scoped to this project only."
                }
            ],
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Boundary = MemoryPlanningBoundary.ReadOnlyEvidence
        };

        var plan = MemoryInformedPlanProposalBuilder.Build(context, bundle);
        var statuses = Enum.GetNames<MemoryInformedPlanStatus>();
        var stepKinds = Enum.GetNames<MemoryInformedPlanStepKind>();

        Assert.AreEqual(MemoryInformedPlanStatus.Proposed, plan.PlanStatus);
        Assert.IsTrue(plan.RequiredHumanReview);
        Assert.IsFalse(statuses.Any(IsForbiddenAuthorityName));
        Assert.IsFalse(stepKinds.Any(IsForbiddenAuthorityName));
        Assert.IsFalse(plan.Boundary.CanApprove);
        Assert.IsFalse(plan.Boundary.CanExecute);
        Assert.IsFalse(plan.Boundary.CanApplySource);
        Assert.IsFalse(plan.Boundary.CanPromoteMemory);
        Assert.IsFalse(plan.Boundary.CanContinueWorkflow);
        Assert.IsTrue(plan.PlanSteps.All(step => !step.Boundary.CanExecute && !step.Boundary.CanApplySource));
    }

    [TestMethod]
    public void BlockAK_KilljoyReview_FlagsAuthorityClaimsButCannotApprove()
    {
        var plan = new MemoryInformedPlanProposal
        {
            PlanProposalId = "plan-authority",
            RunId = "run-ak-review",
            PlannerContextBundleId = "ctx",
            PlanStatus = MemoryInformedPlanStatus.Proposed,
            PlanSteps =
            [
                new MemoryInformedPlanStep
                {
                    StepId = "step-1",
                    StepKind = MemoryInformedPlanStepKind.Plan,
                    Description = "This plan is approved for source apply.",
                    ExpectedEvidence = "Human review remains required.",
                    RequiresGovernedAction = false,
                    Boundary = MemoryPlanningBoundary.ContextEvidence
                }
            ],
            Assumptions = ["approval granted by memory"],
            Risks = [],
            SuggestedTestProfile = SuggestedTestProfileKind.Focused,
            MemoryCitations = ["mem-cite"],
            RequiredHumanReview = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Boundary = MemoryPlanningBoundary.ContextEvidence
        };
        var citations = new MemoryCitationBundle
        {
            MemoryCitationBundleId = "bundle",
            RunId = plan.RunId,
            RetrievalResultId = "retrieval",
            Citations = [],
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Boundary = MemoryPlanningBoundary.ReadOnlyEvidence
        };

        var review = MemoryPlanReviewBuilder.Review(plan, citations);
        var boundary = MemoryPlanReviewBuilder.BuildBoundaryReport(plan, citations);

        Assert.IsTrue(review.AuthorityClaimsFound);
        Assert.AreEqual(KilljoyPlanSeverity.Warning, review.Severity);
        AssertBoundary(review.Boundary);
        CollectionAssert.Contains(boundary.ForbiddenClaimsFound, "AuthorityClaim");
        Assert.IsFalse(boundary.Boundary.CanApprove);
        Assert.IsFalse(boundary.Boundary.CanExecute);
    }

    [TestMethod]
    public async Task BlockAK_PlanCli_WritesRunScopedArtifactsAndBlocksForbiddenVerbs()
    {
        var memoryRoot = CreateTempPath("ak-cli-memory");
        var runPath = CreateTempPath("ak-cli-run");
        var taskPath = Path.Combine(runPath, "task.md");
        try
        {
            var store = new AcceptedMemoryStore(memoryRoot);
            _ = AppendAcceptedMemory(store, MemoryScope.Project, "project-alpha", "Prefer focused validation before source apply.");
            Directory.CreateDirectory(runPath);
            await File.WriteAllTextAsync(taskPath, "Fix the governed memory planning boundary.");

            var memoryContext = await RunCliAsync("plan", "memory-context", "--run", runPath, "--task", taskPath, "--project-id", "project-alpha", "--memory-root", memoryRoot, "--json");
            Assert.AreEqual(0, memoryContext.ExitCode, memoryContext.Error + memoryContext.Output);
            var context = await RunCliAsync("plan", "context", "--run", runPath, "--task", taskPath, "--repo-identity", "repo-alpha", "--base-commit", "abc123", "--relevant-file", "IronDev.Core/Memory/Foo.cs", "--json");
            Assert.AreEqual(0, context.ExitCode, context.Error + context.Output);
            var propose = await RunCliAsync("plan", "propose", "--run", runPath, "--json");
            Assert.AreEqual(0, propose.ExitCode, propose.Error + propose.Output);
            var review = await RunCliAsync("plan", "review", "--run", runPath, "--json");
            Assert.AreEqual(0, review.ExitCode, review.Error + review.Output);
            var status = await RunCliAsync("plan", "status", "--run", runPath, "--json");
            Assert.AreEqual(0, status.ExitCode, status.Error + status.Output);

            foreach (var artifact in new[]
                     {
                         "accepted-memory-retrieval-request.json",
                         "accepted-memory-retrieval-result.json",
                         "memory-context.json",
                         "memory-context.md",
                         "memory-citations.jsonl",
                         "memory-citation-bundle.json",
                         "planner-context-bundle.json",
                         "planner-context.md",
                         "plan-proposal.json",
                         "plan-proposal.md",
                         "plan-risks.md",
                         "suggested-test-profile.json",
                         "planner-boundary-report.json",
                         "planner-boundary-report.md",
                         "killjoy-plan-review.json",
                         "killjoy-plan-review.md",
                         "governance-events.jsonl"
                     })
            {
                Assert.IsTrue(File.Exists(Path.Combine(runPath, artifact)), artifact);
            }

            var events = File.ReadAllText(Path.Combine(runPath, "governance-events.jsonl"));
            StringAssert.Contains(events, nameof(GovernanceKernelEventKind.AcceptedMemoryRetrieved));
            StringAssert.Contains(events, nameof(GovernanceKernelEventKind.MemoryInformedPlanProposed));
            StringAssert.Contains(events, nameof(GovernanceKernelEventKind.KilljoyPlanReviewCreated));

            foreach (var forbidden in new[] { "execute", "apply", "approve", "promote-memory", "continue", "release", "deploy", "merge" })
            {
                var result = await RunCliAsync("plan", forbidden, "--run", runPath);
                Assert.AreEqual(2, result.ExitCode, forbidden);
                StringAssert.Contains(result.Error, "intentionally unsupported");
            }
        }
        finally
        {
            TryDelete(memoryRoot);
            TryDelete(runPath);
        }
    }

    [TestMethod]
    public void BlockAK_StaticBoundary_DoesNotAddSqlApiUiSchedulerOrExecutionSurface()
    {
        var root = FindRepositoryRoot();
        var files = new[]
        {
            Path.Combine(root, "IronDev.Core", "Memory", "MemoryInformedPlanningModels.cs"),
            Path.Combine(root, "tools", "IronDev.Cli", "CliPlanning.cs"),
            Path.Combine(root, "tools", "IronDev.Cli", "IronDevCli.cs")
        };
        var combined = string.Join("\n", files.Select(File.ReadAllText));

        foreach (var forbidden in new[]
                 {
                     "SqlConnection",
                     "DbContext",
                     "ControllerBase",
                     "WebApplication",
                     "IHostedService",
                     "BackgroundService",
                     "ApplySource(",
                     "PromoteMemory(",
                     "ContinueWorkflow(",
                     "CreatePullRequest",
                     "git push",
                     "git commit"
                 })
        {
            Assert.IsFalse(combined.Contains(forbidden, StringComparison.OrdinalIgnoreCase), forbidden);
        }

        var receiptPath = Path.Combine(root, "Docs", "receipts", "PR290_295_MEMORY_INFORMED_PLANNING.md");
        Assert.IsTrue(File.Exists(receiptPath));
        var receipt = File.ReadAllText(receiptPath);
        StringAssert.Contains(receipt, "Memory citations are mandatory.");
        StringAssert.Contains(receipt, "Plan proposal is not authority.");
        StringAssert.Contains(receipt, "Killjoy plan review is not authority.");
    }

    private static AcceptedMemoryAppendResult AppendAcceptedMemory(AcceptedMemoryStore store, MemoryScope scope, string projectId, string content)
    {
        var source = new PatchRunMemorySource
        {
            RunId = "run-memory",
            SourceProjectId = projectId,
            SourceRepoPath = "repo",
            SourceRepoIdentity = "repo-identity",
            RunPath = "run",
            CreatedBy = "human-reviewer"
        };
        var proposal = new MemoryProposal
        {
            MemoryProposalId = $"mem_prop_{Guid.NewGuid():N}",
            RunId = source.RunId,
            SourceProjectId = projectId,
            SourceRepoPath = source.SourceRepoPath,
            SourceRepoIdentity = source.SourceRepoIdentity,
            ProposedScope = scope,
            MemoryKind = MemoryKind.EngineeringLesson,
            ProposedKey = scope == MemoryScope.Project
                ? MemoryKeyNormalizer.BuildProjectKey(projectId, Guid.NewGuid().ToString("N"))
                : scope == MemoryScope.Run
                    ? MemoryKeyNormalizer.BuildRunKey(source.RunId, Guid.NewGuid().ToString("N"))
                    : MemoryKeyNormalizer.BuildPortableEngineeringKey(Guid.NewGuid().ToString("N")),
            Title = "Accepted memory fixture",
            Summary = MemoryContentSafety.SanitiseSummary(content),
            Content = content,
            SanitisedContent = MemoryContentSafety.SanitiseMemoryContent(content),
            EvidenceRefs =
            [
                new MemoryEvidenceRef
                {
                    RefId = "evidence-1",
                    EvidenceKind = MemoryEvidenceKind.ReviewSummary,
                    Path = "review-summary.md",
                    SafeSummary = "Accepted memory fixture evidence.",
                    Sha256 = new string('a', 64)
                }
            ],
            CreatedBy = "human-reviewer",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ProposedConfidence = "High",
            RequiresHumanReview = true,
            SafetyFlags = MemoryContentSafety.Flags(content, scope, source),
            Boundary = MemoryBoundary.None,
            SourceRunPath = source.RunPath
        };
        var request = new MemoryPromotionRequest
        {
            MemoryPromotionRequestId = $"mem_promo_req_{Guid.NewGuid():N}",
            MemoryProposalId = proposal.MemoryProposalId,
            ProposedScope = proposal.ProposedScope,
            ProposedKey = proposal.ProposedKey,
            RequestedBy = "human-reviewer",
            ConscienceDecisionRef = "conscience.json",
            ThoughtLedgerRef = "thought-ledger-ref",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Boundary = MemoryBoundary.None
        };
        var draft = new ConscienceDecision
        {
            DecisionId = $"conscience_{Guid.NewGuid():N}",
            ActionId = $"action_{Guid.NewGuid():N}",
            ActionKind = GovernedActionKind.MemoryPromotion,
            SubjectKind = "MemoryProposal",
            SubjectId = proposal.MemoryProposalId,
            RequestedBy = "human-reviewer",
            EvidenceRefs =
            [
                new ConscienceDecisionEvidenceRef
                {
                    RefId = "evidence-1",
                    EvidenceKind = "ReviewSummary",
                    SafeSummary = "Human reviewed accepted memory fixture."
                }
            ],
            PolicyRefs = ["human-review"],
            RiskLevel = ConscienceDecisionRiskLevel.High,
            Decision = ConscienceDecisionOutcome.Allow,
            BlockReasons = [],
            RequiredHumanReview = true,
            ThoughtLedgerRef = "thought-ledger-ref",
            DecisionHash = string.Empty,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1),
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        var decision = draft with { DecisionHash = ConscienceDecisionHash.Compute(draft) };
        return store.AppendVersion(proposal, request, decision, "thought-ledger-ref");
    }

    private static AcceptedMemoryRetrievalRequest Request(string runId, string projectId) => new()
    {
        RetrievalRequestId = $"retrieval_req_{Guid.NewGuid():N}",
        RunId = runId,
        ProjectId = projectId,
        TaskSummary = "Fix a boundary bug.",
        RepoIdentity = "repo",
        RequestedBy = "tester",
        RequestedAtUtc = DateTimeOffset.UtcNow,
        MaxResults = 10,
        Boundary = MemoryPlanningBoundary.ReadOnlyEvidence
    };

    private static PlannerContextBuildRequest ContextRequest(string runId) => new()
    {
        RunId = runId,
        TaskSummary = "Fix a boundary bug.",
        RepoIdentity = "repo",
        BaseCommit = "abc123",
        RelevantFiles = ["IronDev.Core/Memory/Foo.cs"]
    };

    private static bool IsForbiddenAuthorityName(string name) =>
        name.Contains("Approved", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Executing", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Executed", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Applied", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("ReleaseReady", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("MergeReady", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("DeployReady", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("PolicySatisfied", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("ContinueWorkflow", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("PromoteMemory", StringComparison.OrdinalIgnoreCase);

    private static void AssertBoundary(MemoryPlanningBoundary boundary)
    {
        Assert.IsTrue(boundary.ReadsOnly);
        Assert.IsTrue(boundary.ApprovesNothing);
        Assert.IsTrue(boundary.ExecutesNothing);
        Assert.IsFalse(boundary.CanApprove);
        Assert.IsFalse(boundary.CanExecute);
        Assert.IsFalse(boundary.CanPromoteMemory);
        Assert.IsFalse(boundary.CanApplySource);
        Assert.IsFalse(boundary.CanContinueWorkflow);
        Assert.IsFalse(boundary.CanSatisfyPolicy);
        Assert.IsFalse(boundary.CanApproveRelease);
        Assert.IsFalse(boundary.CanMerge);
        Assert.IsFalse(boundary.CanDeploy);
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunCliAsync(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await IronDevCli.RunAsync(args, output, error, CancellationToken.None).ConfigureAwait(false);
        return (exitCode, output.ToString(), error.ToString());
    }

    private static string CreateTempPath(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort test cleanup only.
        }
    }

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
