using IronDev.Core.Builder;

public sealed class TraceableBuildAgentSpine
{
    private readonly BuildBriefCompiler _briefCompiler = new();
    private readonly ArchitecturePlanner _architecturePlanner = new();
    private readonly FileManifestPlanner _fileManifestPlanner = new();
    private readonly PatchWriter _patchWriter = new();
    private readonly WorkspaceWriter _workspaceWriter = new();
    private readonly BuildRunner _buildRunner = new();
    private readonly TestRunner _testRunner = new();
    private readonly FailureClassifier _failureClassifier = new();
    private readonly RepairPlanner _repairPlanner = new();
    private readonly RetryController _retryController = new();
    private readonly EvidencePackager _evidencePackager = new();

    public TraceableBuildAgentResult CreateSyntheticTrace(string project, string runId, string evidenceRoot)
    {
        var trace = new BuildRunTrace
        {
            RunId = runId,
            Project = project,
            Title = $"{project} Disposable Build",
            SourceSpecIds = ["SOLITAIRE_PRODUCT_SPIKE_ARCHITECTURE_AND_TICKETS_138"],
            SourceTicketIds = ["SOL-139-001"],
            Status = "Running",
            GovernedTier = "Tier5DisposableRepairLoop",
            RealRepoMutationAllowed = false,
            DisposableWorkspaceMutationAllowed = true
        };

        AddStage(trace, "RetrieverAgent", "Context", "Succeeded", "Retrieved Solitaire product spike architecture and rejected BookSeller context.", "Allow");
        trace.Context = new ContextTrace
        {
            TraceId = trace.TraceId,
            Query = "Solitaire disposable build scope",
            SemanticTraceId = Guid.NewGuid().ToString("N"),
            PrimarySourceId = "SOLITAIRE_PRODUCT_SPIKE_ARCHITECTURE_AND_TICKETS_138",
            IncludedSources = ["SOLITAIRE_PRODUCT_SPIKE_ARCHITECTURE_AND_TICKETS_138", "SOLITAIRE_PRODUCT_SPIKE_INTAKE_137"],
            RejectedSources = ["BOOKSELLER_ARCHITECTURE_CURRENT"],
            RiskNotes = ["Solitaire remains a disposable product spike, not production code."],
            AgentFacingSummary = "Build a WPF Klondike vertical slice only inside an explicit disposable workspace."
        };

        AddStage(trace, "ConscienceAgent", "Safety", "Succeeded", "Allowed disposable build only because cage evidence is explicit.", "Allow");
        trace.Conscience = new ConscienceDecisionTrace
        {
            TraceId = trace.TraceId,
            Decision = "Allow",
            Confidence = 0.86m,
            Reasons = ["Disposable workspace boundary is explicit.", "Real repo mutation is blocked.", "Source scope is traceable."],
            AllowingFactors = ["Explicit workspace cage", "Before/after hash requirement", "No self-approval"],
            ObservedProject = project,
            AffectedProject = project,
            AuthoritySources = ["SOLITAIRE_PRODUCT_SPIKE_ARCHITECTURE_AND_TICKETS_138"]
        };

        AddStage(trace, "ThoughtLedger", "Reasoning", "Succeeded", "Explained visible reasoning without hidden chain-of-thought.", "None");
        trace.ThoughtLedger = new ThoughtLedgerTrace
        {
            TraceId = trace.TraceId,
            CurrentBelief = "The build may proceed only inside the disposable workspace cage.",
            EvidenceSummary = ["Conscience decision is Allow.", "Retriever supplied accepted Solitaire scope.", "Real repo writes remain blocked."],
            Assumptions = ["The workspace path is explicit and outside the real repo."],
            TemptingActions = ["Patch the real repo if the disposable build succeeds."],
            BlockedActions = ["real repo write", "memory mutation", "self-approval"],
            SaferAlternatives = ["keep generated files disposable", "package evidence for human/Codex review"],
            RecommendedNextMove = "Run BuilderAgent modules inside the cage and record every attempt."
        };

        AddStage(trace, "BuilderAgent", "Build", "Running", "Compiled build brief, architecture plan, file manifest, patch plan, and retry budget.", "None");
        trace.BuilderPlan = _briefCompiler.Compile(trace.TraceId, project);
        _architecturePlanner.Apply(trace.BuilderPlan);
        _fileManifestPlanner.Apply(trace.BuilderPlan);
        _patchWriter.Apply(trace.BuilderPlan);

        trace.WorkspaceMutation = _workspaceWriter.PlanMutation(trace.TraceId, evidenceRoot);
        trace.DisposableFilesChanged = trace.WorkspaceMutation.ChangedFiles.Count;
        trace.RealRepoMutationCount = trace.WorkspaceMutation.RealRepoMutationCount;

        var build1 = _buildRunner.CreateFailure(trace.TraceId, 1, evidenceRoot);
        trace.BuildAttempts.Add(build1);
        var buildFailure = _failureClassifier.Classify(build1);
        trace.RepairAttempts.Add(_repairPlanner.PlanRepair(trace.TraceId, 1, 1, buildFailure, "Add missing project reference", 1));

        var test1 = _testRunner.CreateFailure(trace.TraceId, 2, evidenceRoot);
        trace.TestAttempts.Add(test1);
        trace.RepairAttempts.Add(_repairPlanner.PlanRepair(trace.TraceId, 2, 2, test1.FailureClassification, "Adjust Klondike tableau King rule", 0));

        var build2 = _buildRunner.CreateSuccess(trace.TraceId, 3, evidenceRoot);
        var test2 = _testRunner.CreateSuccess(trace.TraceId, 3, evidenceRoot);
        trace.BuildAttempts.Add(build2);
        trace.TestAttempts.Add(test2);

        var builderStage = trace.Stages.First(stage => stage.AgentName == "BuilderAgent");
        builderStage.Status = "Succeeded";
        builderStage.Summary = "Recorded synthetic build planning, failures, repairs, and final build/test pass.";

        AddStage(trace, "TesterAgent", "Tests", "Succeeded", "Recorded failed and passing build/test attempts.", "None");
        AddStage(trace, "CriticAgent", "Review", "Skipped", "No failure package review required after final synthetic pass.", "None");
        AddStage(trace, "QualityAgent", "Killjoy", "Skipped", "Quality gate pending for the real 141 build path.", "None");
        AddStage(trace, "SupervisorAgent", "SupervisorSummary", "Succeeded", "Final recommendation is PromoteLater with real repo mutation count zero.", "report_ready");

        trace.Status = "Succeeded";
        trace.Recommendation = "PromoteLater";
        trace.CompletedUtc = DateTimeOffset.UtcNow;
        _retryController.AssertBudget(trace);

        trace.EvidenceArtifacts.AddRange(_evidencePackager.Package(trace.TraceId, evidenceRoot));
        var report = _evidencePackager.CreateReport(trace);
        return new TraceableBuildAgentResult(trace, report);
    }

    private static void AddStage(BuildRunTrace trace, string agent, string stage, string status, string summary, string decision)
    {
        trace.Stages.Add(new AgentStageTrace
        {
            TraceId = trace.TraceId,
            AgentName = agent,
            StageName = stage,
            Status = status,
            Summary = summary,
            Decision = decision,
            CompletedUtc = DateTimeOffset.UtcNow,
            BoundaryNotes = ["No real repository writes.", "Trace does not grant authority."]
        });
    }
}

public sealed record TraceableBuildAgentResult(BuildRunTrace Trace, FinalBuildRunReport Report);

internal sealed class BuildBriefCompiler
{
    public BuilderPlanTrace Compile(string traceId, string project) => new()
    {
        TraceId = traceId,
        BuildBriefId = $"{project}-build-brief-140",
        ProposalId = $"{project}-synthetic-proposal-140",
        SourceSpecId = "SOLITAIRE_PRODUCT_SPIKE_ARCHITECTURE_AND_TICKETS_138",
        Target = "DisposableWorkspaceOnly",
        Assumptions = ["WPF Klondike vertical slice", "click-to-move first", "no persistence"],
        Risks = ["UI is intentionally minimal", "first real run may need repair budget"],
        TestPlan = ["core rule tests", "WPF project build", "real repo hash check"]
    };
}

internal sealed class ArchitecturePlanner
{
    public void Apply(BuilderPlanTrace plan) => plan.PlannedProjects.AddRange(["Solitaire.Core", "Solitaire.Wpf", "Solitaire.Core.Tests"]);
}

internal sealed class FileManifestPlanner
{
    public void Apply(BuilderPlanTrace plan)
    {
        plan.PlannedFiles.AddRange([
            "Solitaire.Core/Card.cs",
            "Solitaire.Core/KlondikeRules.cs",
            "Solitaire.Core/SolitaireGameEngine.cs",
            "Solitaire.Wpf/MainWindow.xaml",
            "Solitaire.Wpf/ViewModels/MainWindowViewModel.cs",
            "Solitaire.Core.Tests/Program.cs"
        ]);
        plan.ForbiddenPaths.AddRange(["real repo root", "Docs/", "tools/dogfood/test-agent-plans/main-alpha-regression-pack.json"]);
    }
}

internal sealed class PatchWriter
{
    public void Apply(BuilderPlanTrace plan) => plan.Risks.Add("PatchWriter may write only under the disposable workspace path.");
}

internal sealed class WorkspaceWriter
{
    public WorkspaceMutationTrace PlanMutation(string traceId, string evidenceRoot) => new()
    {
        TraceId = traceId,
        WorkspacePath = Path.Combine(Path.GetTempPath(), "IronDevDisposableWorkspaces", "synthetic-140", "Solitaire"),
        IsDisposableWorkspace = true,
        IsOutsideRealRepo = true,
        RealRepoBeforeHash = "synthetic-before",
        RealRepoAfterHash = "synthetic-before",
        RealRepoMutationCount = 0,
        ChangedFiles = [
            new() { Path = "Solitaire.Core/KlondikeRules.cs", ChangeType = "Create", ShaAfter = "synthetic-core" },
            new() { Path = "Solitaire.Wpf/MainWindow.xaml", ChangeType = "Create", ShaAfter = "synthetic-wpf" },
            new() { Path = "Solitaire.Core.Tests/Program.cs", ChangeType = "Create", ShaAfter = "synthetic-tests" }
        ]
    };
}

internal sealed class BuildRunner
{
    public BuildAttemptTrace CreateFailure(string traceId, int attempt, string evidenceRoot) => new()
    {
        TraceId = traceId,
        AttemptNumber = attempt,
        Command = "dotnet build Solitaire.Wpf.csproj",
        ExitCode = 1,
        Status = "Failed",
        CompletedUtc = DateTimeOffset.UtcNow,
        StdoutRef = Path.Combine(evidenceRoot, "build-attempt-1.log"),
        Errors = ["CS0246 missing project reference to Solitaire.Core"],
        FailureClassification = "MissingProjectReference"
    };

    public BuildAttemptTrace CreateSuccess(string traceId, int attempt, string evidenceRoot) => new()
    {
        TraceId = traceId,
        AttemptNumber = attempt,
        Command = "dotnet build Solitaire.Wpf.csproj",
        ExitCode = 0,
        Status = "Succeeded",
        CompletedUtc = DateTimeOffset.UtcNow,
        StdoutRef = Path.Combine(evidenceRoot, "build-attempt-3.log")
    };
}

internal sealed class TestRunner
{
    public TestAttemptTrace CreateFailure(string traceId, int attempt, string evidenceRoot) => new()
    {
        TraceId = traceId,
        AttemptNumber = attempt,
        Command = "dotnet test Solitaire.Core.Tests",
        ExitCode = 1,
        Status = "Failed",
        CompletedUtc = DateTimeOffset.UtcNow,
        Passed = 14,
        Failed = 1,
        FailureClassification = "RuleBug",
        FailedTests = ["EmptyTableauAcceptsKing"]
    };

    public TestAttemptTrace CreateSuccess(string traceId, int attempt, string evidenceRoot) => new()
    {
        TraceId = traceId,
        AttemptNumber = attempt,
        Command = "dotnet test Solitaire.Core.Tests",
        ExitCode = 0,
        Status = "Succeeded",
        CompletedUtc = DateTimeOffset.UtcNow,
        Passed = 15,
        Failed = 0
    };
}

internal sealed class FailureClassifier
{
    public string Classify(BuildAttemptTrace attempt) => attempt.FailureClassification;
}

internal sealed class RepairPlanner
{
    public RepairAttemptTrace PlanRepair(string traceId, int repairNumber, int triggerAttempt, string classification, string fix, int retryBudgetRemaining) => new()
    {
        TraceId = traceId,
        RepairAttemptNumber = repairNumber,
        TriggerAttemptNumber = triggerAttempt,
        TriggerFailureClassification = classification,
        PlannedFix = fix,
        FilesAllowed = ["Solitaire.Core/", "Solitaire.Wpf/", "Solitaire.Core.Tests/"],
        FilesChanged = classification == "MissingProjectReference" ? ["Solitaire.Wpf/Solitaire.Wpf.csproj"] : ["Solitaire.Core/KlondikeRules.cs"],
        Status = "Applied",
        Reason = "Synthetic trace proves repair attempts are represented before real heavy BuilderAgent work.",
        RetryBudgetRemaining = retryBudgetRemaining
    };
}

internal sealed class RetryController
{
    public void AssertBudget(BuildRunTrace trace)
    {
        if (trace.RepairAttempts.Any(repair => repair.RetryBudgetRemaining < 0))
            throw new InvalidOperationException("Synthetic repair budget went negative.");
    }
}

internal sealed class EvidencePackager
{
    public IReadOnlyList<EvidenceArtifact> Package(string traceId, string evidenceRoot) =>
    [
        new() { TraceId = traceId, Type = "BuildLog", Path = Path.Combine(evidenceRoot, "build-attempt-1.log"), Summary = "Synthetic missing project reference build failure." },
        new() { TraceId = traceId, Type = "TestResult", Path = Path.Combine(evidenceRoot, "test-attempt-2.log"), Summary = "Synthetic tableau rule test failure." },
        new() { TraceId = traceId, Type = "HashProof", Path = Path.Combine(evidenceRoot, "real-repo-hash-proof.txt"), Summary = "Synthetic real repo before/after hash unchanged." }
    ];

    public FinalBuildRunReport CreateReport(BuildRunTrace trace) => new()
    {
        TraceId = trace.TraceId,
        Title = $"{trace.Project} Disposable Build Report",
        Status = trace.Status,
        Summary = "Synthetic heavy-duty BuilderAgent trace shows context, safety, planning, failed attempts, repairs, final pass, and zero real repo mutation.",
        Timeline = [
            "Retriever packaged Solitaire context.",
            "Conscience allowed disposable-only build.",
            "Builder planned projects/files.",
            "Attempt 1 build failed: MissingProjectReference.",
            "Repair 1 added project reference.",
            "Attempt 2 tests failed: RuleBug.",
            "Repair 2 adjusted KlondikeRules.",
            "Attempt 3 build/test passed."
        ],
        StageStatuses = trace.Stages,
        BuildAttempts = trace.BuildAttempts,
        TestAttempts = trace.TestAttempts,
        RepairAttempts = trace.RepairAttempts,
        RealRepoMutationCount = trace.RealRepoMutationCount,
        DisposableFilesChanged = trace.DisposableFilesChanged,
        Recommendation = trace.Recommendation,
        NextSafeActions = ["Run 141 through the trace spine inside a disposable workspace.", "Keep Killjoy gate enabled before merge."],
        EvidenceRefs = trace.EvidenceArtifacts
    };
}
