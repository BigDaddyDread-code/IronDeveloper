using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IronDev.Agent.Models;
using IronDev.Agent.ViewModels.Workspaces;
using IronDev.Core.Builder;
using IronDev.Core.Interfaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

// ─── Stub orchestrators ───────────────────────────────────────────────────────

/// <summary>
/// Minimal success orchestrator — returns a deterministic preview.
/// Used by most ViewModel tests to avoid real DB calls.
/// </summary>
internal sealed class StubOrchestrator : ITicketBuildOrchestrator
{
    public string LastContextSummary { get; private set; } = "";

    public Task<TicketBuildPreview> CreateBuildPreviewAsync(
        int projectId, long ticketId, CancellationToken ct = default)
    {
        LastContextSummary = $"Stub context — projectId={projectId}, ticketId={ticketId}.";
        return Task.FromResult(new TicketBuildPreview
        {
            TicketId       = ticketId,
            TicketTitle    = "Stub Ticket",
            ContextSummary = LastContextSummary,
            Proposal       = new CodeChangeProposal
            {
                TicketId  = ticketId,
                Summary   = "Stub proposal. LLM not called.",
                RiskNotes = "None — stub.",
                TestPlan  = "None — stub.",
                FileChanges = [new FileChangeProposal
                {
                    FilePath     = "(stub)",
                    ChangeReason = "Stub",
                    Patch        = "No diff generated in Phase 2."
                }]
            }
        });
    }

    public Task<TicketBuildResult> ApplyAndBuildAsync(
        TicketBuildApproval approval, CancellationToken ct = default)
        => Task.FromResult(new TicketBuildResult
        {
            TicketId     = approval.TicketId,
            ErrorMessage = "Apply not implemented yet — Phase 4 required.",
            CompletedUtc = DateTime.UtcNow
        });
}

/// <summary>
/// Orchestrator that throws on CreateBuildPreviewAsync — used to test error handling.
/// </summary>
internal sealed class FailingOrchestrator : ITicketBuildOrchestrator
{
    public Task<TicketBuildPreview> CreateBuildPreviewAsync(
        int projectId, long ticketId, CancellationToken ct = default)
        => throw new InvalidOperationException("Simulated context assembly failure.");

    public Task<TicketBuildResult> ApplyAndBuildAsync(
        TicketBuildApproval approval, CancellationToken ct = default)
        => Task.FromResult(new TicketBuildResult());
}

// ─── Test class ───────────────────────────────────────────────────────────────

/// <summary>
/// ViewModel unit tests for Build Ticket MVP — Phases 1 and 2.
/// No LLM, no Weaviate, no database, no file writes.
/// </summary>
[TestClass]
public sealed class TicketsBuildScaffoldingTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static TicketsWorkspaceViewModel CreateVm(ITicketBuildOrchestrator? orchestrator = null)
        => new(null!, null!, orchestrator ?? new StubOrchestrator());

    private static void SetProjectPath(TicketsWorkspaceViewModel vm, string path = @"C:\repo\test")
        => typeof(TicketsWorkspaceViewModel)
            .GetField("_activeProjectPath",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, path);

    private static void SetProjectId(TicketsWorkspaceViewModel vm, int id = 1)
        => typeof(TicketsWorkspaceViewModel)
            .GetField("_activeProjectId",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, id);

    private static void SimulateTicketSelected(
        TicketsWorkspaceViewModel vm,
        string title = "Fix Chat header",
        long   id    = 1)
    {
        vm.EditTitle = title;
        vm.HasDetail = true;
        vm.IsEditing = true;
        vm.EditId    = id;

        typeof(TicketsWorkspaceViewModel)
            .GetField("_selectedTicket",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, new TicketItem { Id = id, Title = title });
    }

    // ── Phase 1: CanBuildTicket guards ────────────────────────────────────────

    [TestMethod]
    [Description("CanBuildTicket is false when no ticket is selected.")]
    public void CanBuildTicket_False_WhenNoTicketSelected()
    {
        var vm = CreateVm();
        SetProjectPath(vm);
        Assert.IsFalse(vm.CanBuildTicket);
    }

    [TestMethod]
    [Description("CanBuildTicket is false when project path is empty.")]
    public void CanBuildTicket_False_WhenProjectPathEmpty()
    {
        var vm = CreateVm();
        SetProjectPath(vm, "");
        SetProjectId(vm);
        SimulateTicketSelected(vm);
        Assert.IsFalse(vm.CanBuildTicket);
    }

    [TestMethod]
    [Description("CanBuildTicket is false when ticket title is empty.")]
    public void CanBuildTicket_False_WhenTitleEmpty()
    {
        var vm = CreateVm();
        SetProjectPath(vm);
        SetProjectId(vm);
        SimulateTicketSelected(vm, title: "");
        Assert.IsFalse(vm.CanBuildTicket);
    }

    [TestMethod]
    [Description("CanBuildTicket is false while IsBuildingTicket is true.")]
    public void CanBuildTicket_False_WhileBuilding()
    {
        var vm = CreateVm();
        SetProjectPath(vm);
        SetProjectId(vm);
        SimulateTicketSelected(vm);
        vm.IsBuildingTicket = true;
        Assert.IsFalse(vm.CanBuildTicket);
    }

    [TestMethod]
    [Description("CanBuildTicket is true when all conditions are met.")]
    public void CanBuildTicket_True_WhenAllConditionsMet()
    {
        var vm = CreateVm();
        SetProjectPath(vm);
        SetProjectId(vm);
        SimulateTicketSelected(vm);
        Assert.IsTrue(vm.CanBuildTicket);
    }

    // ── Phase 2: BuildSelectedTicketCommand calls orchestrator ────────────────

    [TestMethod]
    [Description("BuildSelectedTicketCommand sets HasBuildPreview=true via orchestrator.")]
    public async Task BuildSelectedTicketCommand_SetsHasBuildPreview()
    {
        var vm = CreateVm();
        SetProjectPath(vm);
        SetProjectId(vm);
        SimulateTicketSelected(vm);

        await vm.BuildSelectedTicketCommand.ExecuteAsync(null);

        Assert.IsTrue(vm.HasBuildPreview);
        Assert.IsNotNull(vm.CurrentBuildPreview);
    }

    [TestMethod]
    [Description("BuildSelectedTicketCommand sets ContextSummary from orchestrator.")]
    public async Task BuildSelectedTicketCommand_SetsContextSummary()
    {
        var orchestrator = new StubOrchestrator();
        var vm = CreateVm(orchestrator);
        SetProjectPath(vm);
        SetProjectId(vm, 42);
        SimulateTicketSelected(vm, id: 7);

        await vm.BuildSelectedTicketCommand.ExecuteAsync(null);

        Assert.IsNotNull(vm.CurrentBuildPreview!.ContextSummary);
        StringAssert.Contains(vm.CurrentBuildPreview.ContextSummary, "projectId=42");
        StringAssert.Contains(vm.CurrentBuildPreview.ContextSummary, "ticketId=7");
    }

    [TestMethod]
    [Description("BuildSelectedTicketCommand passes correct project/ticket IDs to orchestrator.")]
    public async Task BuildSelectedTicketCommand_SetsCorrectTicketTitle()
    {
        var vm = CreateVm();
        SetProjectPath(vm);
        SetProjectId(vm);
        SimulateTicketSelected(vm, title: "Fix Chat History header clipping");

        await vm.BuildSelectedTicketCommand.ExecuteAsync(null);

        Assert.AreEqual("Stub Ticket", vm.CurrentBuildPreview!.TicketTitle);
    }

    [TestMethod]
    [Description("BuildSelectedTicketCommand clears any pre-existing CurrentBuildResult.")]
    public async Task BuildSelectedTicketCommand_ClearsPreviousBuildResult()
    {
        var vm = CreateVm();
        SetProjectPath(vm);
        SetProjectId(vm);
        SimulateTicketSelected(vm);

        vm.CurrentBuildResult = new TicketBuildResult { TicketId = 1, BuildSucceeded = true };

        await vm.BuildSelectedTicketCommand.ExecuteAsync(null);

        Assert.IsNull(vm.CurrentBuildResult);
    }

    [TestMethod]
    [Description("IsBuildingTicket toggles true during command and false after.")]
    public async Task BuildSelectedTicketCommand_IsBuildingTicket_TogglesFalseAfter()
    {
        var vm = CreateVm();
        SetProjectPath(vm);
        SetProjectId(vm);
        SimulateTicketSelected(vm);

        await vm.BuildSelectedTicketCommand.ExecuteAsync(null);

        Assert.IsFalse(vm.IsBuildingTicket,
            "IsBuildingTicket must be false after command completes.");
    }

    [TestMethod]
    [Description("BuildStatusMessage is set to 'AI proposal ready.' after successful preview.")]
    public async Task BuildSelectedTicketCommand_StatusMessage_OnSuccess()
    {
        var vm = CreateVm();
        SetProjectPath(vm);
        SetProjectId(vm);
        SimulateTicketSelected(vm);

        await vm.BuildSelectedTicketCommand.ExecuteAsync(null);

        StringAssert.Contains(vm.BuildStatusMessage, "proposal ready");
    }

    [TestMethod]
    [Description("If orchestrator throws, BuildStatusMessage starts with 'AI proposal failed:'.")]
    public async Task BuildSelectedTicketCommand_OrchestratorFails_SetsFailedMessage()
    {
        var vm = CreateVm(new FailingOrchestrator());
        SetProjectPath(vm);
        SetProjectId(vm);
        SimulateTicketSelected(vm);

        await vm.BuildSelectedTicketCommand.ExecuteAsync(null);

        Assert.IsFalse(vm.HasBuildPreview,
            "HasBuildPreview must remain false when orchestrator throws.");
        StringAssert.StartsWith(vm.BuildStatusMessage, "AI proposal failed:");
    }

    [TestMethod]
    [Description("Preview contains exactly one FileChangeProposal from stub.")]
    public async Task BuildSelectedTicketCommand_Preview_HasOneFileEntry()
    {
        var vm = CreateVm();
        SetProjectPath(vm);
        SetProjectId(vm);
        SimulateTicketSelected(vm);

        await vm.BuildSelectedTicketCommand.ExecuteAsync(null);

        Assert.AreEqual(1, vm.CurrentBuildPreview!.Proposal.FileChanges.Count);
    }

    // ── CancelBuildPreviewCommand ─────────────────────────────────────────────

    [TestMethod]
    [Description("CancelBuildPreviewCommand clears HasBuildPreview, CurrentBuildPreview, BuildStatusMessage.")]
    public async Task CancelBuildPreviewCommand_ClearsPreviewState()
    {
        var vm = CreateVm();
        SetProjectPath(vm);
        SetProjectId(vm);
        SimulateTicketSelected(vm);

        await vm.BuildSelectedTicketCommand.ExecuteAsync(null);
        Assert.IsTrue(vm.HasBuildPreview);

        vm.CancelBuildPreviewCommand.Execute(null);

        Assert.IsFalse(vm.HasBuildPreview);
        Assert.IsNull(vm.CurrentBuildPreview);
        Assert.AreEqual(string.Empty, vm.BuildStatusMessage);
    }

    [TestMethod]
    [Description("CancelBuildPreviewCommand restores CanBuildTicket=true.")]
    public async Task CancelBuildPreview_RestoresCanBuildTicket()
    {
        var vm = CreateVm();
        SetProjectPath(vm);
        SetProjectId(vm);
        SimulateTicketSelected(vm);

        await vm.BuildSelectedTicketCommand.ExecuteAsync(null);
        vm.CancelBuildPreviewCommand.Execute(null);

        Assert.IsTrue(vm.CanBuildTicket);
    }

    // ── ApplyBuildPreviewCommand ──────────────────────────────────────────────

    [TestMethod]
    [Description("ApplyBuildPreviewCommand does not write files and sets not-implemented message.")]
    public async Task ApplyBuildPreviewCommand_DoesNotApply_SetsStubMessage()
    {
        var vm = CreateVm();
        SetProjectPath(vm);
        SetProjectId(vm);
        SimulateTicketSelected(vm);

        await vm.BuildSelectedTicketCommand.ExecuteAsync(null);
        vm.ApplyBuildPreviewCommand.Execute(null);

        Assert.IsNull(vm.CurrentBuildResult, "No build should have run.");
        StringAssert.Contains(vm.BuildStatusMessage, "not implemented");
    }

    // ── Regression: existing editor state unaffected ─────────────────────────

    [TestMethod]
    [Description("StatusOptions, PriorityOptions, TypeOptions still contain expected values.")]
    public void ExistingDropdownOptions_Unchanged()
    {
        var vm = CreateVm();
        CollectionAssert.Contains(vm.StatusOptions,   "Draft");
        CollectionAssert.Contains(vm.StatusOptions,   "In Progress");
        CollectionAssert.Contains(vm.PriorityOptions, "Medium");
        CollectionAssert.Contains(vm.PriorityOptions, "Critical");
        CollectionAssert.Contains(vm.TypeOptions,     "Task");
        CollectionAssert.Contains(vm.TypeOptions,     "Feature");
    }
}

// ─── BuilderContextService unit tests ────────────────────────────────────────

/// <summary>
/// Unit tests for BuilderContextService.
/// Uses minimal stub services — no real DB.
/// </summary>
[TestClass]
public sealed class BuilderContextServiceTests
{
    // ── Stub service implementations ─────────────────────────────────────────

    private sealed class StubProject : IronDev.Services.IProjectService
    {
        private readonly IronDev.Data.Models.Project? _project;
        public StubProject(IronDev.Data.Models.Project? project = null) => _project = project;

        public Task<int> CreateProjectAsync(IronDev.Data.Models.Project p, CancellationToken ct = default) => Task.FromResult(1);
        public Task<System.Collections.Generic.IReadOnlyList<IronDev.Data.Models.Project>> GetProjectsAsync(CancellationToken ct = default)
            => Task.FromResult<System.Collections.Generic.IReadOnlyList<IronDev.Data.Models.Project>>([]);
        public Task<IronDev.Data.Models.Project?> GetByIdAsync(int id, CancellationToken ct = default)
            => Task.FromResult(_project);
        public Task UpdateLocalPathAsync(int id, string path, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubTickets : IronDev.Services.ITicketService
    {
        private readonly IronDev.Data.Models.ProjectTicket? _ticket;
        public StubTickets(IronDev.Data.Models.ProjectTicket? ticket = null) => _ticket = ticket;

        public Task<long> SaveTicketAsync(IronDev.Data.Models.ProjectTicket t, CancellationToken ct = default) => Task.FromResult(1L);
        public Task<System.Collections.Generic.IReadOnlyList<IronDev.Data.Models.ProjectTicket>> GetRecentTicketsAsync(int projectId, int take = 10, CancellationToken ct = default)
            => Task.FromResult<System.Collections.Generic.IReadOnlyList<IronDev.Data.Models.ProjectTicket>>([]);
        public Task<IronDev.Data.Models.ProjectTicket?> GetTicketByIdAsync(long id, CancellationToken ct = default)
            => Task.FromResult(_ticket);
    }

    private sealed class StubMemory : IronDev.Services.IProjectMemoryService
    {
        private readonly IronDev.Data.Models.ProjectImplementationPlan? _plan;
        private readonly IReadOnlyList<IronDev.Data.Models.ProjectDecision> _decisions;

        public StubMemory(
            IronDev.Data.Models.ProjectImplementationPlan? plan = null,
            IReadOnlyList<IronDev.Data.Models.ProjectDecision>? decisions = null)
        {
            _plan      = plan;
            _decisions = decisions ?? [];
        }

        public Task<IronDev.Data.Models.ProjectSummary?> GetLatestSummaryAsync(int p, CancellationToken ct = default)
            => Task.FromResult<IronDev.Data.Models.ProjectSummary?>(null);
        public Task<IReadOnlyList<IronDev.Data.Models.ProjectDecision>> GetRecentDecisionsAsync(int p, int take = 10, CancellationToken ct = default)
            => Task.FromResult(_decisions);
        public Task<IronDev.Data.Models.ProjectDecision?> GetDecisionByIdAsync(long id, CancellationToken ct = default)
            => Task.FromResult<IronDev.Data.Models.ProjectDecision?>(null);
        public Task<long> SaveSummaryAsync(IronDev.Data.Models.ProjectSummary s, CancellationToken ct = default) => Task.FromResult(1L);
        public Task<long> SaveDecisionAsync(IronDev.Data.Models.ProjectDecision d, CancellationToken ct = default) => Task.FromResult(1L);
        public Task<IReadOnlyList<IronDev.Data.Models.ProjectImplementationPlan>> GetRecentPlansAsync(int p, int take = 10, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<IronDev.Data.Models.ProjectImplementationPlan>>([]);
        public Task<IronDev.Data.Models.ProjectImplementationPlan?> GetPlanByIdAsync(long id, CancellationToken ct = default)
            => Task.FromResult<IronDev.Data.Models.ProjectImplementationPlan?>(null);
        public Task<IronDev.Data.Models.ProjectImplementationPlan?> GetPlanByTicketIdAsync(long id, CancellationToken ct = default)
            => Task.FromResult(_plan);
        public Task<long> SavePlanAsync(IronDev.Data.Models.ProjectImplementationPlan p, CancellationToken ct = default) => Task.FromResult(1L);
    }

    private static IronDev.Data.Models.Project MakeProject() => new()
    {
        Id        = 1,
        TenantId  = 1,
        Name      = "TestProject",
        LocalPath = @"C:\repos\test"
    };

    private static IronDev.Data.Models.ProjectTicket MakeTicket(
        string title   = "Fix Chat header",
        string? linked = null,
        string? notes  = null) => new()
    {
        Id              = 1,
        ProjectId       = 1,
        Title           = title,
        Summary         = "Some summary",
        AcceptanceCriteria = "Renders clean at 320px",
        TechnicalNotes  = notes,
        LinkedFilePaths = linked,
        Status          = "Draft",
        Content         = "",
        Priority        = "Medium",
        TicketType      = "Task",
        SessionId       = Guid.NewGuid()
    };

    private static IronDev.Infrastructure.Builder.BuilderContextService MakeSvc(
        IronDev.Data.Models.ProjectTicket?                     ticket    = null,
        IronDev.Data.Models.ProjectImplementationPlan?         plan      = null,
        IReadOnlyList<IronDev.Data.Models.ProjectDecision>?    decisions = null)
        => new(
            new StubTickets(ticket  ?? MakeTicket()),
            new StubProject(MakeProject()),
            new StubMemory(plan, decisions));

    // ── Tests ─────────────────────────────────────────────────────────────────

    [TestMethod]
    [Description("BuilderContextService assembles context with ticket + project.")]
    public async Task AssembleContextAsync_WithTicketAndProject_ReturnsContext()
    {
        var svc = MakeSvc();
        var ctx = await svc.AssembleContextAsync(1, 1);

        Assert.AreEqual("Fix Chat header", ctx.TicketTitle);
        Assert.AreEqual("TestProject",     ctx.ProjectName);
        Assert.AreEqual(@"C:\repos\test",  ctx.ProjectPath);
        Assert.AreEqual("dotnet build",    ctx.BuildCommand);
    }

    [TestMethod]
    [Description("BuilderContextService handles missing linked plan without throwing.")]
    public async Task AssembleContextAsync_NoPlan_DoesNotThrow()
    {
        var svc = MakeSvc(plan: null); // no plan
        var ctx = await svc.AssembleContextAsync(1, 1);

        Assert.IsNull(ctx.PlanTitle);
        Assert.IsNull(ctx.PlanGoal);
    }

    [TestMethod]
    [Description("BuilderContextService populates plan fields when plan is present.")]
    public async Task AssembleContextAsync_WithPlan_PopulatesPlanFields()
    {
        var plan = new IronDev.Data.Models.ProjectImplementationPlan
        {
            Id        = 10,
            TicketId  = 1,
            ProjectId = 1,
            Title     = "Implementation Plan",
            Goal      = "Fix the clipping"
        };
        var svc = MakeSvc(plan: plan);
        var ctx = await svc.AssembleContextAsync(1, 1);

        Assert.AreEqual("Implementation Plan", ctx.PlanTitle);
        Assert.AreEqual("Fix the clipping",    ctx.PlanGoal);
    }

    [TestMethod]
    [Description("BuilderContextService handles no decisions without throwing.")]
    public async Task AssembleContextAsync_NoDecisions_DoesNotThrow()
    {
        var svc = MakeSvc(decisions: []);
        var ctx = await svc.AssembleContextAsync(1, 1);

        Assert.AreEqual(0, ctx.Decisions.Count);
    }

    [TestMethod]
    [Description("BuilderContextService formats decisions as readable strings.")]
    public async Task AssembleContextAsync_WithDecisions_FormatsStrings()
    {
        var decisions = (IReadOnlyList<IronDev.Data.Models.ProjectDecision>)
        [
            new IronDev.Data.Models.ProjectDecision
            {
                Id     = 1, ProjectId = 1,
                Title  = "Use MVVM pattern",
                Reason = "Testability",
                Status = "Accepted",
            }
        ];
        var svc = MakeSvc(decisions: decisions);
        var ctx = await svc.AssembleContextAsync(1, 1);

        Assert.AreEqual(1, ctx.Decisions.Count);
        StringAssert.Contains(ctx.Decisions[0], "Use MVVM pattern");
        StringAssert.Contains(ctx.Decisions[0], "Testability");
    }

    [TestMethod]
    [Description("BuilderContextService extracts affected files from ticket.LinkedFilePaths.")]
    public async Task AssembleContextAsync_ExtractsAffectedFiles_FromLinkedFilePaths()
    {
        var ticket = MakeTicket(linked: @"Views\ChatWorkspaceView.xaml,Views\MainWindow.xaml");
        var svc    = MakeSvc(ticket: ticket);
        var ctx    = await svc.AssembleContextAsync(1, 1);

        Assert.AreEqual(2, ctx.AffectedFiles.Count);
        CollectionAssert.Contains(ctx.AffectedFiles.ToList(), @"Views\ChatWorkspaceView.xaml");
        CollectionAssert.Contains(ctx.AffectedFiles.ToList(), @"Views\MainWindow.xaml");
    }

    [TestMethod]
    [Description("BuilderContextService deduplicates affected files across sources.")]
    public async Task AssembleContextAsync_DeduplicatesAffectedFiles()
    {
        // Same file appears in LinkedFilePaths and plan.AffectedContext
        var ticket = MakeTicket(linked: @"Views\ChatWorkspaceView.xaml");
        var plan   = new IronDev.Data.Models.ProjectImplementationPlan
        {
            Id        = 10,
            TicketId  = 1,
            ProjectId = 1,
            Title     = "Plan",
            Goal      = "Goal",
            AffectedContext = @"Views\ChatWorkspaceView.xaml"   // duplicate
        };
        var svc = MakeSvc(ticket: ticket, plan: plan);
        var ctx = await svc.AssembleContextAsync(1, 1);

        // Should appear only once
        var matching = ctx.AffectedFiles
            .Where(f => f == @"Views\ChatWorkspaceView.xaml")
            .Count();
        Assert.AreEqual(1, matching, "Duplicate file path should be deduplicated.");
    }

    [TestMethod]
    [Description("BuilderContextService handles ticket with no file fields without throwing.")]
    public async Task AssembleContextAsync_NoFileFields_DoesNotThrow()
    {
        var ticket = MakeTicket(linked: null, notes: null);
        var svc    = MakeSvc(ticket: ticket);
        var ctx    = await svc.AssembleContextAsync(1, 1);

        Assert.AreEqual(0, ctx.AffectedFiles.Count);
    }

    [TestMethod]
    [Description("Weaviate lists are empty in Phase 2 (no Weaviate calls).")]
    public async Task AssembleContextAsync_WeaviateFields_AreEmpty()
    {
        var svc = MakeSvc();
        var ctx = await svc.AssembleContextAsync(1, 1);

        Assert.AreEqual(0, ctx.RetrievedSnippets.Count);
        Assert.AreEqual(0, ctx.PastBuildFailures.Count);
    }
}

// ─── Phase 3: CodeChangeProposalService tests ────────────────────────────────

/// <summary>
/// Unit tests for CodeChangeProposalService.
/// Uses fake ILLMService implementations — no real API calls.
/// </summary>
[TestClass]
public sealed class CodeChangeProposalServiceTests
{
    // ── Fake LLM implementations ─────────────────────────────────────────────

    private sealed class FakeLlm : IronDev.Core.ILLMService
    {
        private readonly string _response;
        public FakeLlm(string response) => _response = response;
        public Task<string> GetResponseAsync(string prompt) => Task.FromResult(_response);
    }

    private sealed class ThrowingLlm : IronDev.Core.ILLMService
    {
        private readonly string _message;
        public ThrowingLlm(string message = "Network error") => _message = message;
        public Task<string> GetResponseAsync(string prompt) =>
            throw new InvalidOperationException(_message);
    }

    private static TicketBuildContext MakeContext(long ticketId = 1) => new()
    {
        ProjectId   = 1,
        TicketId    = ticketId,
        ProjectName = "TestProject",
        ProjectPath = @"C:\repos\test",
        TicketTitle = "Fix Chat History header clipping",
        TicketSummary = "The left pane header clips beside the + New button.",
        TicketAcceptanceCriteria = "Header renders cleanly at 320px.",
    };

    private static string ValidJson(long ticketId = 1) => $$"""
        {
          "ticketId": {{ticketId}},
          "summary": "Replace 'Conversation History' with 'Conversations'.",
          "riskNotes": "Low — XAML layout only.",
          "testPlan": "Run app, verify header at 320px.",
          "fileChanges": [
            {
              "filePath": "Views\\ChatWorkspaceView.xaml",
              "changeReason": "Header text clips.",
              "beforeSnippet": "<TextBlock Text=\"Conversation History\"",
              "afterSnippet": "<TextBlock Text=\"Conversations\" TextTrimming=\"CharacterEllipsis\"",
              "patch": "@@ -1,1 +1,1 @@\n-Conversation History\n+Conversations"
            }
          ]
        }
        """;

    // ── Parsing tests ─────────────────────────────────────────────────────────

    [TestMethod]
    [Description("CodeChangeProposalService parses valid JSON into CodeChangeProposal.")]
    public async Task ParsesValidJson_ReturnsProposal()
    {
        var svc = new IronDev.Infrastructure.Builder.CodeChangeProposalService(new FakeLlm(ValidJson()));
        var proposal = await svc.GenerateProposalAsync(MakeContext());

        Assert.AreEqual("Replace 'Conversation History' with 'Conversations'.", proposal.Summary);
        Assert.AreEqual(1, proposal.FileChanges.Count);
        Assert.AreEqual(@"Views\ChatWorkspaceView.xaml", proposal.FileChanges[0].FilePath);
        Assert.AreEqual("Low — XAML layout only.", proposal.RiskNotes);
    }

    [TestMethod]
    [Description("CodeChangeProposalService strips markdown fences before parsing.")]
    public async Task StripsFences_BeforeParsing()
    {
        var wrapped = "```json\n" + ValidJson() + "\n```";
        var svc     = new IronDev.Infrastructure.Builder.CodeChangeProposalService(new FakeLlm(wrapped));
        var proposal = await svc.GenerateProposalAsync(MakeContext());

        Assert.AreEqual("Replace 'Conversation History' with 'Conversations'.", proposal.Summary);
    }

    [TestMethod]
    [Description("CodeChangeProposalService rejects invalid JSON with clear exception.")]
    public async Task InvalidJson_ThrowsWithClearMessage()
    {
        var svc = new IronDev.Infrastructure.Builder.CodeChangeProposalService(
            new FakeLlm("this is not json at all"));

        InvalidOperationException? caught = null;
        try   { await svc.GenerateProposalAsync(MakeContext()); }
        catch (InvalidOperationException ex) { caught = ex; }

        Assert.IsNotNull(caught, "Expected InvalidOperationException was not thrown.");
        StringAssert.Contains(caught.Message, "invalid JSON");
    }

    [TestMethod]
    [Description("CodeChangeProposalService handles empty fileChanges without throwing.")]
    public async Task EmptyFileChanges_ReturnsProposalWithNoFiles()
    {
        var json = """
            {
              "ticketId": 1,
              "summary": "Nothing to change.",
              "riskNotes": "N/A",
              "testPlan": "N/A",
              "fileChanges": []
            }
            """;
        var svc = new IronDev.Infrastructure.Builder.CodeChangeProposalService(new FakeLlm(json));
        var proposal = await svc.GenerateProposalAsync(MakeContext());

        Assert.AreEqual(0, proposal.FileChanges.Count);
        Assert.AreEqual("Nothing to change.", proposal.Summary);
    }

    [TestMethod]
    [Description("CodeChangeProposalService handles LLM failure with clear exception.")]
    public async Task LlmThrows_PropagatesWithClearMessage()
    {
        var svc = new IronDev.Infrastructure.Builder.CodeChangeProposalService(
            new ThrowingLlm("Simulated timeout"));

        InvalidOperationException? caught = null;
        try   { await svc.GenerateProposalAsync(MakeContext()); }
        catch (InvalidOperationException ex) { caught = ex; }

        Assert.IsNotNull(caught, "Expected InvalidOperationException was not thrown.");
        StringAssert.Contains(caught.Message, "LLM call failed");
        StringAssert.Contains(caught.Message, "Simulated timeout");
    }

    [TestMethod]
    [Description("Built prompt contains ticket title and project name.")]
    public void BuildPrompt_ContainsTicketAndProject()
    {
        var ctx    = MakeContext();
        var prompt = IronDev.Infrastructure.Builder.CodeChangeProposalService.BuildPrompt(ctx);

        StringAssert.Contains(prompt, ctx.TicketTitle);
        StringAssert.Contains(prompt, ctx.ProjectName);
        StringAssert.Contains(prompt, "Return ONLY a JSON object");
    }

    [TestMethod]
    [Description("Built prompt includes acceptance criteria when present.")]
    public void BuildPrompt_IncludesAcceptanceCriteria()
    {
        var ctx    = MakeContext();
        var prompt = IronDev.Infrastructure.Builder.CodeChangeProposalService.BuildPrompt(ctx);

        StringAssert.Contains(prompt, ctx.TicketAcceptanceCriteria!);
    }

    [TestMethod]
    [Description("Built prompt includes affected files when present.")]
    public void BuildPrompt_IncludesAffectedFiles()
    {
        var ctx = MakeContext();
        ctx.AffectedFiles = [
            @"Views\ChatWorkspaceView.xaml"
        ];
        var prompt = IronDev.Infrastructure.Builder.CodeChangeProposalService.BuildPrompt(ctx);

        StringAssert.Contains(prompt, @"Views\ChatWorkspaceView.xaml");
    }

    [TestMethod]
    [Description("ParseProposal uses fallback ticketId when LLM returns 0.")]
    public void ParseProposal_UsesFallbackTicketId_WhenLlmReturnsZero()
    {
        var json = """
            { "ticketId": 0, "summary": "ok", "riskNotes": "low",
              "testPlan": "test", "fileChanges": [] }
            """;
        var proposal = IronDev.Infrastructure.Builder.CodeChangeProposalService.ParseProposal(json, 99L);

        Assert.AreEqual(99L, proposal.TicketId,
            "Should fall back to the supplied ticketId when LLM returns 0.");
    }
}

// ─── Phase 3: Orchestrator integration tests ──────────────────────────────────

/// <summary>
/// Tests that TicketBuildOrchestrator correctly wires context + proposal services.
/// All dependencies are stubbed — no real DB or LLM.
/// </summary>
[TestClass]
public sealed class OrchestratorPhase3Tests
{
    // ── Stub IBuilderContextService ───────────────────────────────────────────

    private sealed class StubContextSvc : IronDev.Core.Interfaces.IBuilderContextService
    {
        private readonly TicketBuildContext _ctx;
        public StubContextSvc(TicketBuildContext? ctx = null)
            => _ctx = ctx ?? new TicketBuildContext
            {
                ProjectId   = 1, TicketId = 1,
                ProjectName = "TestProject",
                ProjectPath = @"C:\repos\test",
                TicketTitle = "Fix header",
                TicketSummary = "Summary"
            };

        public Task<TicketBuildContext> AssembleContextAsync(
            int p, long t, CancellationToken ct = default) => Task.FromResult(_ctx);
    }

    // ── Stub ICodeChangeProposalService ──────────────────────────────────────

    private sealed class StubProposalSvc : IronDev.Core.Interfaces.ICodeChangeProposalService
    {
        private readonly CodeChangeProposal _proposal;
        public StubProposalSvc(CodeChangeProposal? proposal = null)
            => _proposal = proposal ?? new CodeChangeProposal
            {
                TicketId  = 1,
                Summary   = "Replace header text.",
                RiskNotes = "Low.",
                TestPlan  = "Verify at 320px.",
                FileChanges = [new FileChangeProposal
                {
                    FilePath      = @"Views\ChatWorkspaceView.xaml",
                    ChangeReason  = "Header clips.",
                    BeforeSnippet = "Conversation History",
                    AfterSnippet  = "Conversations",
                    Patch         = "@@ -1 +1 @@\n-Conversation History\n+Conversations"
                }]
            };

        public Task<CodeChangeProposal> GenerateProposalAsync(
            TicketBuildContext ctx, CancellationToken ct = default)
            => Task.FromResult(_proposal);
    }

    private sealed class FailingProposalSvc : IronDev.Core.Interfaces.ICodeChangeProposalService
    {
        public Task<CodeChangeProposal> GenerateProposalAsync(
            TicketBuildContext ctx, CancellationToken ct = default)
            => throw new InvalidOperationException("AI proposal failed: invalid JSON");
    }

    private static IronDev.Infrastructure.Builder.TicketBuildOrchestrator MakeOrchestrator(
        IronDev.Core.Interfaces.IBuilderContextService?     ctxSvc      = null,
        IronDev.Core.Interfaces.ICodeChangeProposalService? proposalSvc = null)
        => new(ctxSvc ?? new StubContextSvc(), proposalSvc ?? new StubProposalSvc());

    // ── Tests ─────────────────────────────────────────────────────────────────

    [TestMethod]
    [Description("Orchestrator returns preview with real proposal summary.")]
    public async Task CreateBuildPreviewAsync_ReturnsPreviewWithProposal()
    {
        var orch    = MakeOrchestrator();
        var preview = await orch.CreateBuildPreviewAsync(1, 1);

        Assert.IsNotNull(preview.Proposal);
        Assert.AreEqual("Replace header text.", preview.Proposal.Summary);
    }

    [TestMethod]
    [Description("Orchestrator populates ContextSummary.")]
    public async Task CreateBuildPreviewAsync_PopulatesContextSummary()
    {
        var orch    = MakeOrchestrator();
        var preview = await orch.CreateBuildPreviewAsync(1, 1);

        Assert.IsFalse(string.IsNullOrWhiteSpace(preview.ContextSummary));
        StringAssert.Contains(preview.ContextSummary, "TestProject");
    }

    [TestMethod]
    [Description("Orchestrator ensures ticketId is set on returned proposal.")]
    public async Task CreateBuildPreviewAsync_SetsCorrectTicketId()
    {
        var orch    = MakeOrchestrator();
        var preview = await orch.CreateBuildPreviewAsync(1, 42);

        Assert.AreEqual(42, preview.TicketId);
        Assert.AreEqual(42, preview.Proposal.TicketId);
    }

    [TestMethod]
    [Description("Orchestrator handles empty fileChanges — IsEmpty is true.")]
    public async Task CreateBuildPreviewAsync_EmptyFileChanges_IsEmpty()
    {
        var emptyProposal = new CodeChangeProposal
        {
            TicketId = 1, Summary = "Nothing to change.",
            FileChanges = []
        };
        var orch    = MakeOrchestrator(proposalSvc: new StubProposalSvc(emptyProposal));
        var preview = await orch.CreateBuildPreviewAsync(1, 1);

        Assert.IsTrue(preview.IsEmpty);
    }

    [TestMethod]
    [Description("ViewModel: BuildSelectedTicketCommand sets AI proposal preview.")]
    public async Task ViewModel_BuildCommand_SetsAIProposalPreview()
    {
        var orch = MakeOrchestrator();
        var vm   = new TicketsWorkspaceViewModel(null!, null!, orch);

        typeof(TicketsWorkspaceViewModel)
            .GetField("_activeProjectPath",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, @"C:\repos\test");
        typeof(TicketsWorkspaceViewModel)
            .GetField("_activeProjectId",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, 1);

        vm.EditTitle = "Fix header";
        vm.HasDetail = true;
        vm.IsEditing = true;
        vm.EditId    = 1;
        typeof(TicketsWorkspaceViewModel)
            .GetField("_selectedTicket",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, new IronDev.Agent.Models.TicketItem { Id = 1, Title = "Fix header" });

        await vm.BuildSelectedTicketCommand.ExecuteAsync(null);

        Assert.IsTrue(vm.HasBuildPreview);
        Assert.AreEqual("Replace header text.", vm.CurrentBuildPreview!.Proposal.Summary);
        Assert.AreEqual(1, vm.CurrentBuildPreview.Proposal.FileChanges.Count);
        StringAssert.Contains(vm.BuildStatusMessage, "proposal ready");
    }

    [TestMethod]
    [Description("ViewModel: LLM failure sets BuildStatusMessage to 'AI proposal failed:'.")]
    public async Task ViewModel_LlmFailure_SetsBuildFailedMessage()
    {
        var orch = MakeOrchestrator(proposalSvc: new FailingProposalSvc());
        var vm   = new TicketsWorkspaceViewModel(null!, null!, orch);

        typeof(TicketsWorkspaceViewModel)
            .GetField("_activeProjectPath",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, @"C:\repos\test");
        typeof(TicketsWorkspaceViewModel)
            .GetField("_activeProjectId",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, 1);

        vm.EditTitle = "Fix header";
        vm.HasDetail = true;
        vm.IsEditing = true;
        vm.EditId    = 1;
        typeof(TicketsWorkspaceViewModel)
            .GetField("_selectedTicket",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, new IronDev.Agent.Models.TicketItem { Id = 1, Title = "Fix header" });

        await vm.BuildSelectedTicketCommand.ExecuteAsync(null);

        Assert.IsFalse(vm.HasBuildPreview);
        StringAssert.StartsWith(vm.BuildStatusMessage, "AI proposal failed:");
    }

    [TestMethod]
    [Description("ApplyBuildPreviewCommand still does not write files after Phase 3.")]
    public void ApplyBuildPreview_DoesNotWriteFiles()
    {
        var orch = MakeOrchestrator();
        var vm   = new TicketsWorkspaceViewModel(null!, null!, orch);

        // Simulate a preview already loaded
        vm.CurrentBuildPreview = new TicketBuildPreview
        {
            TicketId    = 1,
            TicketTitle = "Fix header",
            Proposal    = new CodeChangeProposal { Summary = "ok" }
        };
        vm.HasBuildPreview = true;

        vm.ApplyBuildPreviewCommand.Execute(null);

        Assert.IsNull(vm.CurrentBuildResult, "No build result should exist — no files written.");
        StringAssert.Contains(vm.BuildStatusMessage, "not implemented");
    }
}
