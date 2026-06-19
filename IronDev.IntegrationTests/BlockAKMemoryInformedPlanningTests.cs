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
            var projectItem = result.Items.Single(item => item.MemoryId == project.Record.MemoryId);
            Assert.AreEqual("run-memory", projectItem.SourceRunId);
            Assert.AreEqual("human-reviewer", projectItem.AcceptedBy);
            Assert.AreEqual(project.Version.ContentHash, projectItem.ContentHash);
            StringAssert.StartsWith(projectItem.CitationRef, "accepted-memory:");
            Assert.IsFalse(string.IsNullOrWhiteSpace(projectItem.SafeSummary));
            AssertBoundary(result.Boundary);
        }
        finally
        {
            TryDelete(root);
            TryDelete(run);
        }
    }

    [TestMethod]
    public void BlockAK_AcceptedMemoryRetriever_LoadsLegacyAcceptedMemoryWithoutNewMetadata()
    {
        var root = CreateTempPath("ak-legacy-memory");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "accepted-memory-versions", "mem_legacy"));
            File.WriteAllText(Path.Combine(root, "accepted-memory-index.json"),
                """
                {
                  "records": [
                    {
                      "memoryId": "mem_legacy",
                      "scope": "Project",
                      "memoryKind": "EngineeringLesson",
                      "projectId": "project-alpha",
                      "key": "project:project-alpha:legacy",
                      "currentVersion": 1,
                      "createdAtUtc": "2026-01-01T00:00:00+00:00",
                      "updatedAtUtc": "2026-01-02T00:00:00+00:00",
                      "isSuperseded": false
                    }
                  ]
                }
                """);
            File.WriteAllText(Path.Combine(root, "accepted-memory-versions", "mem_legacy", "1.json"),
                """
                {
                  "memoryVersionId": "mem_ver_legacy",
                  "memoryId": "mem_legacy",
                  "version": 1,
                  "memoryKind": "EngineeringLesson",
                  "proposalId": "legacy-proposal",
                  "promotionRequestId": "legacy-promotion",
                  "conscienceDecisionId": "legacy-decision",
                  "thoughtLedgerRef": "legacy-ledger",
                  "content": "Use old memory safely.",
                  "sanitisedContent": "Use old memory safely.",
                  "evidenceRefs": [],
                  "boundary": {}
                }
                """);

            var result = AcceptedMemoryRetriever.Retrieve(new AcceptedMemoryStore(root), Request("run-ak-legacy", "project-alpha"));
            var item = result.Items.Single();

            Assert.AreEqual("legacy-proposal", item.SourceRunId);
            Assert.AreEqual("unknown-reviewer", item.AcceptedBy);
            Assert.AreEqual(MemoryContentSafety.ContentHash("Use old memory safely."), item.ContentHash);
            Assert.AreEqual(DateTimeOffset.Parse("2026-01-02T00:00:00+00:00"), item.AcceptedAtUtc);
        }
        finally
        {
            TryDelete(root);
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

            var context = PlannerContextBuilder.Build(ContextRequest("run-ak-citations"), store, partialBundle);

            Assert.AreEqual(1, context.AcceptedMemoryRefs.Length);
            Assert.AreEqual(partialBundle.MemoryCitationBundleId, context.MemoryCitationBundleId);
            Assert.IsTrue(context.IgnoredMemory.Any(item => item.StartsWith("accepted-memory:", StringComparison.OrdinalIgnoreCase)));
            StringAssert.Contains(context.ContextBoundary, "cannot approve");
            Assert.IsFalse(context.KnownConstraints.Any(item => item.Contains("approval granted", StringComparison.OrdinalIgnoreCase)));
            AssertBoundary(context.Boundary);
            var validation = MemoryCitationValidator.Validate(partialBundle, retrieval, "project-alpha");
            Assert.IsTrue(validation.IsValid, string.Join(",", validation.Issues));
            var storeValidation = MemoryCitationValidator.Validate(partialBundle, store, "project-alpha");
            Assert.IsTrue(storeValidation.IsValid, string.Join(",", storeValidation.Issues));
            foreach (var citation in partialBundle.Citations)
            {
                Assert.AreEqual(MemoryKind.EngineeringLesson, citation.MemoryKind);
                Assert.AreEqual("run-memory", citation.SourceRunId);
                Assert.AreEqual("human-reviewer", citation.AcceptedBy);
                Assert.AreEqual(first.Version.ContentHash, citation.ContentHash);
                Assert.IsFalse(string.IsNullOrWhiteSpace(citation.RelevanceReason));
                Assert.IsFalse(MemoryCitationWriter.IsAuthorityUse(citation.UsedFor));
                Assert.IsFalse(citation.Caveat.Contains("approval", StringComparison.OrdinalIgnoreCase));
            }

            var wrongId = partialBundle with
            {
                Citations = [partialBundle.Citations[0] with { MemoryId = "mem-not-retrieved" }]
            };
            CollectionAssert.Contains(MemoryCitationValidator.Validate(wrongId, retrieval, "project-alpha").Issues, "UnknownMemoryId:mem-not-retrieved");

            var wrongHash = partialBundle with
            {
                Citations = [partialBundle.Citations[0] with { ContentHash = new string('b', 64) }]
            };
            Assert.IsTrue(MemoryCitationValidator.Validate(wrongHash, retrieval, "project-alpha").Issues.Any(issue => issue.StartsWith("ContentHashMismatch", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(MemoryCitationValidator.Validate(wrongHash, store, "project-alpha").Issues.Any(issue => issue.StartsWith("ContentHashMismatch", StringComparison.OrdinalIgnoreCase)));

            var wrongProject = partialBundle with
            {
                Citations = [partialBundle.Citations[0] with { ProjectId = "project-other" }]
            };
            Assert.IsTrue(MemoryCitationValidator.Validate(wrongProject, retrieval, "project-alpha").Issues.Any(issue => issue.StartsWith("ProjectScopeMismatch", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [TestMethod]
    public void BlockAK_MemoryKind_DrivesCitationUseAndPlannerContext()
    {
        var root = CreateTempPath("ak-memory-kind");
        try
        {
            var store = new AcceptedMemoryStore(root);
            var risk = AppendAcceptedMemory(store, MemoryScope.Project, "project-alpha", "Boundary fixes can regress authority checks.", MemoryKind.RiskPattern);
            var test = AppendAcceptedMemory(store, MemoryScope.Project, "project-alpha", "Run focused governance tests for authority fixes.", MemoryKind.TestCommandLesson);
            var review = AppendAcceptedMemory(store, MemoryScope.Project, "project-alpha", "Review heuristics should check for hidden authority language.", MemoryKind.ReviewHeuristic);
            var ai = AppendAcceptedMemory(store, MemoryScope.Project, "project-alpha", "AI assistance should propose smaller patch shapes.", MemoryKind.AiAssistanceLesson);

            var retrieval = AcceptedMemoryRetriever.Retrieve(store, Request("run-ak-kind", "project-alpha"));
            var citations = MemoryCitationWriter.CreateBundle(retrieval);
            var context = PlannerContextBuilder.Build(ContextRequest("run-ak-kind"), retrieval, citations);

            Assert.AreEqual(MemoryKind.RiskPattern, retrieval.Items.Single(item => item.MemoryId == risk.Record.MemoryId).MemoryKind);
            Assert.AreEqual(MemoryKind.TestCommandLesson, retrieval.Items.Single(item => item.MemoryId == test.Record.MemoryId).MemoryKind);
            Assert.AreEqual(MemoryKind.ReviewHeuristic, retrieval.Items.Single(item => item.MemoryId == review.Record.MemoryId).MemoryKind);
            Assert.AreEqual(MemoryKind.AiAssistanceLesson, retrieval.Items.Single(item => item.MemoryId == ai.Record.MemoryId).MemoryKind);

            Assert.AreEqual(MemoryCitationUsedFor.RiskWarning, citations.Citations.Single(item => item.MemoryId == risk.Record.MemoryId).UsedFor);
            Assert.AreEqual(MemoryCitationUsedFor.TestSuggestion, citations.Citations.Single(item => item.MemoryId == test.Record.MemoryId).UsedFor);
            Assert.AreEqual(MemoryCitationUsedFor.ConstraintReminder, citations.Citations.Single(item => item.MemoryId == review.Record.MemoryId).UsedFor);
            Assert.AreEqual(MemoryCitationUsedFor.PatchStrategy, citations.Citations.Single(item => item.MemoryId == ai.Record.MemoryId).UsedFor);

            Assert.IsTrue(context.KnownRisks.Any(item => item.Contains("authority checks", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(context.SuggestedTestHints.Any(item => item.Contains(citations.Citations.Single(citation => citation.MemoryId == test.Record.MemoryId).CitationId, StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(context.KnownConstraints.Any(item => item.Contains("hidden authority language", StringComparison.OrdinalIgnoreCase)));
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
            ProjectId = "project-alpha",
            TaskSummary = "Add a small boundary fix.",
            CurrentGoal = "Add a small boundary fix.",
            RepoIdentity = "repo",
            BaseCommit = "abc123",
            RelevantFiles = ["IronDev.Core/Foo.cs"],
            AcceptedMemoryRefs = ["mem-cite-1"],
            MemoryCitationBundleId = "bundle-1",
            Constraints = ["Do not add authority."],
            KnownRisks = ["Boundary fixes need focused tests."],
            KnownConstraints = ["Project memory is scoped to this project only."],
            SuggestedTestHints = ["Run focused boundary tests."],
            IgnoredMemory = [],
            ContextBoundary = "Planner context is advisory and cannot approve or execute.",
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
                    MemoryKind = MemoryKind.EngineeringLesson,
                    MemoryScope = MemoryScope.Project,
                    ProjectId = "project-alpha",
                    SourceRunId = "run-memory",
                    AcceptedAtUtc = DateTimeOffset.UtcNow,
                    AcceptedBy = "human-reviewer",
                    SafeSummary = "Use narrow scoped patches.",
                    ContentHash = new string('a', 64),
                    RelevanceReason = "Accepted memory provides planning context.",
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
        Assert.AreEqual("project-alpha", plan.ProjectId);
        Assert.AreEqual("Add a small boundary fix.", plan.Goal);
        Assert.IsTrue(plan.RequiredHumanReview);
        Assert.IsTrue(plan.EvidenceRequirements.Any(item => item.Contains("Human review", StringComparison.OrdinalIgnoreCase)));
        StringAssert.Contains(plan.NonAuthorityBoundary, "not executable");
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
            ProjectId = "project-alpha",
            Goal = "Review a bad authority-shaped plan.",
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
            EvidenceRequirements = ["Human review remains required."],
            SuggestedTestProfile = SuggestedTestProfileKind.Focused,
            MemoryCitations = ["mem-cite"],
            RequiredHumanReview = true,
            NonAuthorityBoundary = "This plan proposal is not approved or executable.",
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
        Assert.AreEqual(KilljoyPlanDecision.Blocked, review.Decision);
        Assert.AreEqual(KilljoyPlanSeverity.Blocker, review.Severity);
        Assert.IsTrue(review.StructuredFindings.Any(item => item.Category == "AuthorityLeak"));
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
            var context = await RunCliAsync("plan", "context", "--run", runPath, "--task", taskPath, "--project-id", "project-alpha", "--memory-root", memoryRoot, "--repo-identity", "repo-alpha", "--base-commit", "abc123", "--relevant-file", "IronDev.Core/Memory/Foo.cs", "--json");
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
                         "accepted-memory-retrieval.json",
                         "accepted-memory-retrieval-result.json",
                         "memory-context.json",
                         "memory-context.md",
                         "memory-citations.json",
                         "memory-citations.jsonl",
                         "memory-citation-bundle.json",
                         "planner-context.json",
                         "planner-context-bundle.json",
                         "planner-context.md",
                         "memory-informed-plan.json",
                         "plan-proposal.json",
                         "plan-proposal.md",
                         "plan-risk-report.json",
                         "plan-risks.md",
                         "suggested-test-profile.json",
                         "planning-boundary-report.json",
                         "planner-boundary-report.json",
                         "planner-boundary-report.md",
                         "killjoy-plan-review.json",
                         "killjoy-plan-review.md",
                         "governance-events.jsonl"
                     })
            {
                Assert.IsTrue(File.Exists(Path.Combine(runPath, artifact)), artifact);
            }

            var retrieval = ReadJson<AcceptedMemoryRetrievalResult>(Path.Combine(runPath, "accepted-memory-retrieval.json"));
            Assert.AreEqual("human-reviewer", retrieval!.Items.Single().AcceptedBy);
            var plan = ReadJson<MemoryInformedPlanProposal>(Path.Combine(runPath, "memory-informed-plan.json"));
            StringAssert.Contains(plan!.NonAuthorityBoundary, "not executable");
            var killjoy = ReadJson<KilljoyPlanReview>(Path.Combine(runPath, "killjoy-plan-review.json"));
            Assert.AreEqual(KilljoyPlanDecision.NoBlockingIssuesFound, killjoy!.Decision);

            var events = File.ReadAllText(Path.Combine(runPath, "governance-events.jsonl"));
            StringAssert.Contains(events, nameof(GovernanceKernelEventKind.AcceptedMemoryRetrieved));
            StringAssert.Contains(events, nameof(GovernanceKernelEventKind.MemoryInformedPlanProposed));
            StringAssert.Contains(events, nameof(GovernanceKernelEventKind.KilljoyPlanReviewCreated));

            foreach (var forbidden in new[] { "execute", "apply", "rollback", "approve", "promote-memory", "append-memory", "continue", "release", "deploy", "merge", "commit", "push", "pull-request" })
            {
                var result = await RunCliAsync("plan", forbidden, "--run", runPath);
                Assert.AreEqual(2, result.ExitCode, forbidden);
                StringAssert.Contains(result.Error, "intentionally unsupported");
            }

            var forgedRunPath = CreateTempPath("ak-cli-forged-run");
            try
            {
                Directory.CreateDirectory(forgedRunPath);
                var forgedContent = "Forged memory should not enter planner context.";
                var forgedRetrieval = new AcceptedMemoryRetrievalResult
                {
                    RetrievalResultId = "mem_retrieval_forged",
                    RetrievalRequestId = "mem_retrieval_req_forged",
                    RunId = Path.GetFileName(forgedRunPath),
                    RetrievedAtUtc = DateTimeOffset.UtcNow,
                    Items =
                    [
                        new MemoryContextItem
                        {
                            MemoryId = "mem_forged",
                            MemoryVersionId = "mem_ver_forged",
                            MemoryScope = MemoryScope.Project,
                            MemoryKind = MemoryKind.EngineeringLesson,
                            ProjectId = "project-alpha",
                            SourceRunId = "run-forged",
                            AcceptedBy = "forged-reviewer",
                            ContentSummary = forgedContent,
                            SafeSummary = forgedContent,
                            SafeContent = forgedContent,
                            CitationRef = "accepted-memory:mem_forged:mem_ver_forged",
                            ContentHash = MemoryContentSafety.ContentHash(forgedContent),
                            SourceEvidenceRefs = [],
                            AcceptedAtUtc = DateTimeOffset.UtcNow,
                            LastVerifiedAtUtc = DateTimeOffset.UtcNow,
                            Staleness = MemoryPlanningStaleness.Current,
                            Confidence = "AcceptedMemoryEvidence",
                            Caveats = ["Project memory is scoped to this project only."]
                        }
                    ],
                    Boundary = MemoryPlanningBoundary.ReadOnlyEvidence
                };
                File.WriteAllText(Path.Combine(forgedRunPath, "accepted-memory-retrieval-result.json"), JsonSerializer.Serialize(forgedRetrieval, JsonOptions));
                File.WriteAllText(Path.Combine(forgedRunPath, "memory-citation-bundle.json"), JsonSerializer.Serialize(MemoryCitationWriter.CreateBundle(forgedRetrieval), JsonOptions));

                var forgedContext = await RunCliAsync("plan", "context", "--run", forgedRunPath, "--task", taskPath, "--project-id", "project-alpha", "--memory-root", memoryRoot, "--json");
                Assert.AreEqual(1, forgedContext.ExitCode, forgedContext.Output + forgedContext.Error);
                StringAssert.Contains(forgedContext.Output + forgedContext.Error, "UnknownMemoryId:mem_forged");
            }
            finally
            {
                TryDelete(forgedRunPath);
            }

            var oneShotRunPath = CreateTempPath("ak-cli-one-shot-run");
            try
            {
                var oneShot = await RunCliAsync("memory", "plan", "--run", oneShotRunPath, "--memory-root", memoryRoot, "--project", "project-alpha", "--task", taskPath, "--repo-identity", "repo-alpha", "--base-commit", "abc123", "--json");
                Assert.AreEqual(0, oneShot.ExitCode, oneShot.Error + oneShot.Output);
                AssertArtifactExists(oneShotRunPath, "accepted-memory-retrieval.json");
                AssertArtifactExists(oneShotRunPath, "memory-informed-plan.json");
                AssertArtifactExists(oneShotRunPath, "killjoy-plan-review.json");
            }
            finally
            {
                TryDelete(oneShotRunPath);
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
            Path.Combine(root, "tools", "IronDev.Cli", "CliPlanning.cs")
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
                     "CreatePullRequestAsync",
                     "PullRequestService",
                     "ProcessStartInfo",
                     "RunProcessAsync"
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
        StringAssert.Contains(receipt, "AK1 reads accepted memory only.");
        StringAssert.Contains(receipt, "AK2 requires citations for memory influence.");
        StringAssert.Contains(receipt, "AK3 builds planner context.");
        StringAssert.Contains(receipt, "AK4 creates a plan proposal.");
        StringAssert.Contains(receipt, "AK5 reviews the plan for authority leaks.");
        StringAssert.Contains(receipt, "AK6 proves memory cannot bypass authority.");
    }

    private static AcceptedMemoryAppendResult AppendAcceptedMemory(AcceptedMemoryStore store, MemoryScope scope, string projectId, string content, MemoryKind kind = MemoryKind.EngineeringLesson)
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
            MemoryKind = kind,
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
        ProjectId = "project-alpha",
        TaskSummary = "Fix a boundary bug.",
        CurrentGoal = "Fix a boundary bug.",
        RepoIdentity = "repo",
        BaseCommit = "abc123",
        Constraints = ["Do not add authority."],
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

    private static void AssertArtifactExists(string path, string artifact) =>
        Assert.IsTrue(File.Exists(Path.Combine(path, artifact)), artifact);

    private static T? ReadJson<T>(string path) =>
        File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions) : default;

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
