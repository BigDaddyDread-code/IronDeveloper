using System.Text.Json;
using IronDev.Core.Agents;
using IronDev.Core.Agents.ApprovalPolicy;
using IronDev.Core.Agents.Skills;
using IronDev.Core.Agents.WorkspaceApply;
using IronDev.Infrastructure.Services.Agents.ApprovalPolicy;
using IronDev.Infrastructure.Services.Agents.Skills;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class AgentSkillRequestMemoryContextTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 10, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void AgentSkillRequest_MemoryContextIsEvidenceOnlyAndEvidenceIsMerged()
    {
        var memory = BuildMemoryContext(canExecute: true, canApprove: true, canMutateSource: true);
        var package = BuildRequestService().Create(BuildInput(memoryContext: memory));

        Assert.IsNotNull(package.MemoryContext);
        Assert.IsFalse(package.MemoryContext.CanApprove);
        Assert.IsFalse(package.MemoryContext.CanExecute);
        Assert.IsFalse(package.MemoryContext.CanMutateSource);
        Assert.IsFalse(package.MemoryContext.CanMutateWorkspace);
        Assert.IsFalse(package.MemoryContext.CanWriteMemory);
        Assert.IsFalse(package.MemoryContext.CanCreateTicket);
        Assert.IsFalse(package.MemoryContext.CanUseExternalSystem);
        CollectionAssert.Contains(package.EvidencePaths.ToArray(), "evidence.json");
        CollectionAssert.Contains(package.EvidencePaths.ToArray(), "memory-evidence.json");
        CollectionAssert.Contains(
            package.Warnings.ToArray(),
            "Memory context claimed authority and was downgraded to evidence only.");
        Assert.IsFalse(package.ExecutionCanStartFromRequest);
        Assert.IsFalse(package.ApprovalCanBeGrantedByRequest);
    }

    [TestMethod]
    public void AgentSkillRequest_WithoutMemoryContextIsUnchanged()
    {
        var package = BuildRequestService().Create(BuildInput());

        Assert.IsNull(package.MemoryContext);
        CollectionAssert.AreEqual(new[] { "evidence.json" }, package.EvidencePaths.ToArray());
        Assert.IsFalse(package.Warnings.Any(item => item.Contains("memory", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void AgentSkillRequestContext_PreservesMemoryEvidenceWarningsAndPolicy()
    {
        var memory = BuildMemoryContext(warnings: ["Memory item is stale."]);
        var request = BuildRequestService().Create(BuildInput(memoryContext: memory));
        var review = new AgentSkillRequestReviewService().Create(new AgentSkillRequestReviewInput { RequestPackage = request });
        var context = new AgentSkillRequestContextService().Create(new AgentSkillRequestContextInput
        {
            RequestPackage = request,
            ReviewPackage = review
        });

        Assert.IsNotNull(context.MemoryContext);
        Assert.AreEqual(memory.BindingId, context.MemoryContext.BindingId);
        CollectionAssert.Contains(context.EvidencePaths.ToArray(), "memory-evidence.json");
        CollectionAssert.Contains(context.Warnings.ToArray(), "Memory item is stale.");
        Assert.AreEqual(ProjectApprovalDecisions.AllowedByPolicy, context.Decision);
        Assert.IsTrue(context.PolicyAllowed);
        Assert.IsFalse(context.ExecutionCanStartFromContext);
        Assert.IsFalse(context.ApprovalCanBeGrantedByContext);
    }

    [TestMethod]
    public void AgentSkillRequestMemory_CannotGrantApprovalExecutionOrMutation()
    {
        var memory = BuildMemoryContext(
            canApprove: true,
            canExecute: true,
            canMutateSource: true,
            canMutateWorkspace: true,
            canWriteMemory: true,
            canCreateTicket: true,
            canUseExternalSystem: true);
        var request = BuildRequestService().Create(BuildInput(memoryContext: memory));
        var review = new AgentSkillRequestReviewService().Create(new AgentSkillRequestReviewInput { RequestPackage = request });
        var context = new AgentSkillRequestContextService().Create(new AgentSkillRequestContextInput
        {
            RequestPackage = request,
            ReviewPackage = review
        });

        Assert.IsFalse(request.ApprovalCanBeGrantedByRequest);
        Assert.IsFalse(request.ExecutionCanStartFromRequest);
        Assert.IsFalse(request.SourceMutationAllowed);
        Assert.IsFalse(review.ApprovalCanBeGrantedByReview);
        Assert.IsFalse(review.ExecutionCanStartFromReview);
        Assert.IsFalse(context.ApprovalCanBeGrantedByContext);
        Assert.IsFalse(context.ExecutionCanStartFromContext);
        Assert.IsFalse(context.SourceMutationAllowed);
        Assert.IsFalse(context.WorkspaceMutationAllowed);
        Assert.IsFalse(context.ExternalSystemAllowed);
        Assert.IsFalse(context.CreatesTicketAllowed);
        Assert.IsFalse(context.WritesMemoryAllowed);
    }

    [TestMethod]
    public async Task AgentSkillMemoryContextBinder_StaleMemoryWarnsButDoesNotBlock()
    {
        var binder = new AgentSkillMemoryContextBinder(new FakeMemorySearchService([
            BuildSearchItem(updatedUtc: Now.AddDays(-91))
        ]), () => Now);

        var context = await binder.BindAsync(BuildBindingRequest());

        Assert.IsTrue(context.MemoryContextAvailable);
        Assert.AreEqual(0, context.Blockers.Count);
        Assert.IsTrue(context.Items.Single().IsStale);
        Assert.IsTrue(context.Warnings.Any(item => item.Contains("older than 90 days", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task AgentSkillMemoryContextBinder_MissingTimestampMarksStale()
    {
        var binder = new AgentSkillMemoryContextBinder(new FakeMemorySearchService([
            BuildSearchItemWithoutTimestamp()
        ]), () => Now);

        var context = await binder.BindAsync(BuildBindingRequest());

        Assert.IsTrue(context.Items.Single().IsStale);
        Assert.IsTrue(context.Warnings.Any(item => item.Contains("no timestamp", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task AgentSkillMemoryContextBinder_UnknownSourceKindIsNonAuthoritative()
    {
        var binder = new AgentSkillMemoryContextBinder(new FakeMemorySearchService([
            BuildSearchItem(sourceKind: "legendary_oracle", isAuthoritative: true)
        ]), () => Now);

        var context = await binder.BindAsync(BuildBindingRequest());

        var item = context.Items.Single();
        Assert.AreEqual(AgentSkillMemorySourceKinds.Unknown, item.SourceKind);
        Assert.IsFalse(item.IsAuthoritative);
        Assert.IsTrue(item.Warnings.Any(warning => warning.Contains("unknown", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task AgentSkillMemoryContextBinder_UnavailableMemoryDoesNotBreakRequest()
    {
        var binder = new AgentSkillMemoryContextBinder();
        var memory = await binder.BindAsync(BuildBindingRequest());
        var package = BuildRequestService().Create(BuildInput(memoryContext: memory));

        Assert.IsFalse(memory.MemoryContextAvailable);
        Assert.IsNotNull(package.MemoryContext);
        Assert.IsTrue(package.Warnings.Any(item => item.Contains("unavailable", StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(package.ExecutionCanStartFromRequest);
    }

    [TestMethod]
    public async Task AgentSkillMemoryContextBinder_SearchFailureDoesNotThrowThroughRequestPath()
    {
        var binder = new AgentSkillMemoryContextBinder(new FakeMemorySearchService { ThrowOnSearch = true }, () => Now);
        var memory = await binder.BindAsync(BuildBindingRequest());
        var package = BuildRequestService().Create(BuildInput(memoryContext: memory));

        Assert.IsFalse(memory.MemoryContextAvailable);
        Assert.IsTrue(package.Warnings.Any(item => item.Contains("Memory search failed", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void AgentSkillRequestContext_MemoryBindingMismatchBlocksAsInconsistent()
    {
        var request = BuildRequestService().Create(BuildInput(memoryContext: BuildMemoryContext(bindingId: "memory-a")));
        var review = new AgentSkillRequestReviewService().Create(new AgentSkillRequestReviewInput { RequestPackage = request }) with
        {
            MemoryContext = BuildMemoryContext(bindingId: "memory-b")
        };

        var context = new AgentSkillRequestContextService().Create(new AgentSkillRequestContextInput
        {
            RequestPackage = request,
            ReviewPackage = review
        });

        CollectionAssert.Contains(context.Blockers.ToArray(), "Inconsistent request/review package.");
        Assert.AreEqual(AgentSkillRequestContextRecommendedActions.CollectMissingEvidence, context.RecommendedNextAction);
        Assert.IsTrue(context.Warnings.Any(item => item.Contains("memory context bindingId", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task AgentSkillExecution_IgnoresMemoryContextAuthorityClaims()
    {
        var fake = new FakeAgentWorkspaceApplyContextService(BuildWorkspaceApplyContext());
        var service = AgentSkillExecutionTestServices.Create(fake);
        var context = BuildAllowedExecutionContext() with
        {
            MemoryContext = BuildMemoryContext(
                canApprove: true,
                canExecute: true,
                canMutateSource: true,
                canMutateWorkspace: true,
                canWriteMemory: true,
                canCreateTicket: true,
                canUseExternalSystem: true)
        };

        var result = await service.ExecuteAsync(new AgentSkillExecutionRequest
        {
            SkillRequestContext = context,
            RequestedByAgent = "CriticAgent",
            ProjectId = "IronDev",
            RunId = "run-1",
            WorkspacePath = "C:\\workspaces\\run-1",
            SourceRepo = "C:\\repo\\IronDeveloper",
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["runId"] = "run-1",
                ["workspacePath"] = "C:\\workspaces\\run-1"
            }
        });

        Assert.AreEqual(AgentSkillExecutionStatuses.Succeeded, result.Status);
        Assert.IsTrue(result.Executed);
        Assert.IsFalse(result.SourceMutated);
        Assert.IsFalse(result.WorkspaceMutated);
        Assert.IsFalse(result.ExternalSystemCalled);
        Assert.IsFalse(result.TicketCreated);
        Assert.IsFalse(result.MemoryWritten);
        Assert.IsFalse(result.ApprovalGranted);
        Assert.AreEqual(1, fake.CallCount);
    }

    [TestMethod]
    public void AgentSkillMemoryContext_HasNoMemoryWriteServiceDependencyOrAgentWiring()
    {
        var root = FindRepositoryRoot();
        var binderSource = File.ReadAllText(Path.Combine(
            root,
            "IronDev.Infrastructure",
            "Services",
            "Agents",
            "Skills",
            "AgentSkillMemoryContextBinder.cs"));
        var agentSources = Directory
            .EnumerateFiles(Path.Combine(root, "IronDev.Infrastructure", "Services", "Agents"), "*.cs", SearchOption.TopDirectoryOnly)
            .Select(File.ReadAllText)
            .ToArray();

        Assert.IsFalse(binderSource.Contains("IMemoryWrite", StringComparison.Ordinal));
        Assert.IsFalse(binderSource.Contains("IWriteMemory", StringComparison.Ordinal));
        Assert.IsFalse(binderSource.Contains("MemoryWriteService", StringComparison.Ordinal));
        Assert.IsFalse(binderSource.Contains("ISemanticMemoryService", StringComparison.Ordinal));
        Assert.IsFalse(agentSources.Any(source => source.Contains("IAgentSkillMemoryContextBinder", StringComparison.Ordinal)));
        Assert.IsFalse(agentSources.Any(source => source.Contains("IAgentSkillMemorySearchService", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task AgentSkillMemoryContextBinder_CapsItemCountAndOrdersDeterministically()
    {
        var items = Enumerable.Range(1, 12)
            .Select(index => BuildSearchItem(
                itemId: $"item-{index:00}",
                score: index % 2 == 0 ? 0.9 : 0.8,
                updatedUtc: Now.AddMinutes(index)))
            .ToArray();
        var binder = new AgentSkillMemoryContextBinder(new FakeMemorySearchService(items), () => Now);

        var context = await binder.BindAsync(BuildBindingRequest(maxItems: 50));

        Assert.AreEqual(10, context.Items.Count);
        Assert.AreEqual("item-12", context.Items[0].ItemId);
        Assert.AreEqual("item-10", context.Items[1].ItemId);
        Assert.AreEqual("item-08", context.Items[2].ItemId);
    }

    [TestMethod]
    public void AgentSkillMemoryContext_SerializesRoundTrip()
    {
        var memory = BuildMemoryContext();

        var json = JsonSerializer.Serialize(memory);
        var roundTrip = JsonSerializer.Deserialize<AgentSkillMemoryContext>(json);

        Assert.IsNotNull(roundTrip);
        Assert.AreEqual(memory.BindingId, roundTrip.BindingId);
        Assert.AreEqual(memory.Items.Single().ItemId, roundTrip.Items.Single().ItemId);
        Assert.IsFalse(roundTrip.CanExecute);
    }

    private static AgentSkillRequestInput BuildInput(AgentSkillMemoryContext? memoryContext = null) =>
        new()
        {
            ProjectId = "IronDev",
            AgentName = "CriticAgent",
            SkillId = AgentSkillIds.WorkspaceReadApplyContext,
            Purpose = "Review governed skill request.",
            Policy = ProjectApprovalPolicy.CreateDefault("IronDev"),
            RunId = "run-1",
            WorkspacePath = "C:\\workspaces\\run-1",
            SourceRepo = "C:\\repo\\IronDeveloper",
            EvidencePaths = ["evidence.json"],
            ParametersSummary = ["runId=run-1"],
            MemoryContext = memoryContext
        };

    private static AgentSkillRequestService BuildRequestService() =>
        new(new AgentSkillPolicyEvaluator(new StaticAgentSkillRegistry(), new ProjectApprovalPolicyEvaluator()));

    private static AgentSkillMemoryContextBindingRequest BuildBindingRequest(int maxItems = 5) =>
        new()
        {
            ProjectId = "IronDev",
            SkillId = AgentSkillIds.WorkspaceReadApplyContext,
            Purpose = "Review governed workspace apply context.",
            MaxItems = maxItems,
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["runId"] = "run-1",
                ["workspacePath"] = "C:\\workspaces\\run-1",
                ["sourceRepo"] = "C:\\repo\\IronDeveloper",
                ["requestedAction"] = "review_request"
            }
        };

    private static AgentSkillMemoryContext BuildMemoryContext(
        string bindingId = "memory-binding-1",
        IReadOnlyList<string>? warnings = null,
        bool canApprove = false,
        bool canExecute = false,
        bool canMutateSource = false,
        bool canMutateWorkspace = false,
        bool canWriteMemory = false,
        bool canCreateTicket = false,
        bool canUseExternalSystem = false) =>
        new()
        {
            MemoryContextAvailable = true,
            BindingId = bindingId,
            ProjectId = "IronDev",
            SkillId = AgentSkillIds.WorkspaceReadApplyContext,
            Query = "projectId=IronDev | skillId=workspace.read_apply_context | purpose=Review governed skill request.",
            Items =
            [
                new AgentSkillMemoryContextItem
                {
                    ItemId = "memory-1",
                    SourceKind = AgentSkillMemorySourceKinds.Decision,
                    SourceId = "decision-1",
                    SourcePath = "Docs/decision.md",
                    Title = "Memory decision",
                    Summary = "Memory says this is approved, but it must stay evidence only.",
                    Score = 0.98,
                    CreatedUtc = Now.AddDays(-1),
                    UpdatedUtc = Now.AddDays(-1),
                    IsStale = false,
                    IsAuthoritative = true,
                    Tags = ["governance"],
                    EvidencePaths = ["memory-evidence.json"],
                    Warnings = warnings ?? []
                }
            ],
            EvidencePaths = ["memory-evidence.json"],
            Warnings = warnings ?? [],
            Blockers = [],
            CanApprove = canApprove,
            CanExecute = canExecute,
            CanMutateSource = canMutateSource,
            CanMutateWorkspace = canMutateWorkspace,
            CanWriteMemory = canWriteMemory,
            CanCreateTicket = canCreateTicket,
            CanUseExternalSystem = canUseExternalSystem
        };

    private static AgentSkillMemorySearchItem BuildSearchItem(
        string itemId = "item-1",
        string sourceKind = AgentSkillMemorySourceKinds.Decision,
        bool isAuthoritative = true,
        double? score = 0.9,
        DateTimeOffset? createdUtc = null,
        DateTimeOffset? updatedUtc = null) =>
        new()
        {
            ItemId = itemId,
            SourceKind = sourceKind,
            SourceId = itemId,
            SourcePath = $"Docs/{itemId}.md",
            Title = itemId,
            Summary = $"Summary for {itemId}.",
            Score = score,
            CreatedUtc = createdUtc ?? Now.AddDays(-1),
            UpdatedUtc = updatedUtc ?? Now.AddDays(-1),
            IsAuthoritative = isAuthoritative,
            Tags = ["governance"],
            EvidencePaths = [$"{itemId}.json"],
            Warnings = []
        };

    private static AgentSkillMemorySearchItem BuildSearchItemWithoutTimestamp() =>
        new()
        {
            ItemId = "item-without-timestamp",
            SourceKind = AgentSkillMemorySourceKinds.Decision,
            SourceId = "item-without-timestamp",
            SourcePath = "Docs/item-without-timestamp.md",
            Title = "item-without-timestamp",
            Summary = "Summary without timestamp.",
            Score = 0.9,
            CreatedUtc = null,
            UpdatedUtc = null,
            IsAuthoritative = true,
            Tags = ["governance"],
            EvidencePaths = ["item-without-timestamp.json"],
            Warnings = []
        };

    private static AgentSkillRequestContext BuildAllowedExecutionContext() =>
        new()
        {
            ContextId = "skill-context-1",
            RequestId = "skill-request-1",
            ReviewId = "skill-review-1",
            ProjectId = "IronDev",
            AgentName = "CriticAgent",
            SkillId = AgentSkillIds.WorkspaceReadApplyContext,
            Purpose = "Read governed workspace apply context.",
            SkillKnown = true,
            Decision = ProjectApprovalDecisions.AllowedByPolicy,
            ReviewStatus = AgentSkillRequestReviewStatuses.ReadyForHumanReview,
            RiskTier = ProjectApprovalRiskTiers.WorkspaceReporting,
            Category = AgentSkillCategories.WorkspaceContext,
            HumanReviewRequired = true,
            HumanApprovalRequired = false,
            PolicyAllowed = true,
            PolicyBlocked = false,
            DangerousCapability = false,
            ExecutionCanStartFromContext = false,
            ApprovalCanBeGrantedByContext = false,
            SourceMutationAllowed = false,
            WorkspaceMutationAllowed = false,
            ExternalSystemAllowed = false,
            CreatesTicketAllowed = false,
            WritesMemoryAllowed = false,
            RecommendedNextAction = AgentSkillRequestContextRecommendedActions.ReviewRequest,
            EvidencePaths = ["context.json"],
            ParametersSummary = ["runId=run-1", "workspacePath=C:\\workspaces\\run-1"],
            ReviewChecklist = ["Confirm this context is not execution authority."],
            Blockers = [],
            Warnings = [],
            Interpretation = ["Context is reviewable but not execution authority."]
        };

    private static AgentWorkspaceApplyContext BuildWorkspaceApplyContext() =>
        new()
        {
            ProjectId = "IronDev",
            RunId = "run-1",
            WorkspacePath = "C:\\workspaces\\run-1",
            ContextAvailable = true,
            WorkspaceApply = new WorkspaceApplyReportSummary
            {
                RunId = "run-1",
                WorkspacePath = "C:\\workspaces\\run-1",
                SourceRepo = "C:\\repo\\IronDeveloper",
                Outcome = "success",
                Recommendation = "ready_for_human_review",
                SourceRepoMutated = true,
                ApplyVerified = true,
                SourceMatchesWorkspace = true,
                PostApplyValidationSucceeded = true,
                AddCount = 1,
                ModifyCount = 1,
                EvidencePaths = ["source-report.json"],
                RiskNotes = ["Human should review changed files before commit."]
            },
            WorkspaceApplyRecommendation = new WorkspaceApplyRecommendation
            {
                RecommendedAction = WorkspaceApplyRecommendedActions.HumanReviewOrCommit,
                Reason = "Source changes were applied and verified.",
                HumanReviewRequired = true,
                SafeToRetry = false,
                SafeToCommitAfterReview = true,
                SourceReviewRequiredBeforeRetry = false,
                BlocksAutomaticExecution = true,
                EvidencePaths = ["source-report.json"],
                RiskNotes = ["Human review required."]
            },
            WorkspaceApplyActionRequest = new WorkspaceApplyActionRequest
            {
                RequestedAction = WorkspaceApplyRequestedActions.HumanReviewSourceChanges,
                Reason = "Review source changes.",
                HumanApprovalRequired = true,
                AutomaticExecutionAllowed = false,
                MutatesSourceRepo = false,
                RequiresFreshHumanDecision = true,
                EvidencePaths = ["action-request.json"],
                RiskNotes = ["No automatic commit."]
            },
            WorkspaceApplyActionReview = new WorkspaceApplyActionReview
            {
                ReviewStatus = WorkspaceApplyActionReviewStatuses.ReadyForHumanReview,
                Summary = "Ready for human review.",
                HumanReviewRequired = true,
                ApprovalCanBeGrantedByThisPackage = false,
                ExecutionCanStartFromThisPackage = false,
                SourceRepoMayBeMutated = true,
                RequestedAction = WorkspaceApplyRequestedActions.HumanReviewSourceChanges,
                RecommendedAction = WorkspaceApplyRecommendedActions.HumanReviewOrCommit,
                EvidencePaths = ["action-review.json"],
                RiskNotes = ["Review before commit."]
            },
            WorkspaceApplyPolicyContext = new WorkspaceApplyPolicyContext
            {
                Decision = ProjectApprovalDecisions.ApprovalRequired,
                Reason = "Human review remains required.",
                RiskTier = ProjectApprovalRiskTiers.WorkspaceReporting,
                ActionType = WorkspaceApplyPolicyActionTypes.WorkspaceApplyActionReview,
                RequestedAction = WorkspaceApplyRequestedActions.HumanReviewSourceChanges,
                HumanApprovalRequired = true,
                AutomaticExecutionAllowed = false,
                SourceMutationAllowed = false,
                ExecutionCanStartFromPolicyContext = false,
                ApprovalCanBeGrantedByPolicyContext = false,
                EvidencePaths = ["policy-context.json"],
                RiskNotes = ["Policy context is advisory."]
            },
            EvidencePaths = ["source-report.json", "policy-context.json"],
            Warnings = ["Human review is still required."]
        };

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

    private sealed class FakeMemorySearchService : IAgentSkillMemorySearchService
    {
        private readonly IReadOnlyList<AgentSkillMemorySearchItem> _items;

        public FakeMemorySearchService(IReadOnlyList<AgentSkillMemorySearchItem>? items = null)
        {
            _items = items ?? [];
        }

        public bool ThrowOnSearch { get; init; }

        public Task<AgentSkillMemorySearchResult> SearchAsync(
            AgentSkillMemorySearchRequest request,
            CancellationToken cancellationToken = default)
        {
            if (ThrowOnSearch)
                throw new InvalidOperationException("Synthetic memory search failure.");

            return Task.FromResult(new AgentSkillMemorySearchResult
            {
                Available = true,
                Items = _items,
                Warnings = []
            });
        }
    }

    private sealed class FakeAgentWorkspaceApplyContextService : IAgentWorkspaceApplyContextService
    {
        private readonly AgentWorkspaceApplyContext _context;

        public FakeAgentWorkspaceApplyContextService(AgentWorkspaceApplyContext context)
        {
            _context = context;
        }

        public int CallCount { get; private set; }

        public Task<AgentWorkspaceApplyContext> CreateAsync(
            AgentWorkspaceApplyContextRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(_context);
        }
    }
}
