using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IronDev.Agent.Models;
using IronDev.Agent.ViewModels.Workspaces;
using IronDev.Core.Builder;
using IronDev.Core.Interfaces;
using IronDev.Data.Models;
using IronDev.Core.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

internal sealed class StubProfile : IProjectProfileService
{
    public Task<ProjectProfile?> GetProjectProfileAsync(int projectId, CancellationToken ct = default)
        => Task.FromResult<ProjectProfile?>(new ProjectProfile { ProjectId = projectId, ApplicationType = "WPF" });

    public Task SaveProjectProfileAsync(ProjectProfile profile, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<List<ProjectCommand>> GetProjectCommandsAsync(int projectId, CancellationToken ct = default)
        => Task.FromResult(new List<ProjectCommand>());

    public Task SaveProjectCommandAsync(ProjectCommand command, CancellationToken ct = default) => Task.CompletedTask;

    public Task<ProjectCommand?> GetDefaultCommandAsync(int projectId, string category, CancellationToken ct = default)
        => Task.FromResult<ProjectCommand?>(null);

    public Task<List<ProjectProfileOption>> GetOptionsByCategoryAsync(string category, CancellationToken ct = default)
        => Task.FromResult(new List<ProjectProfileOption>());
}

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
            ValidationResult = new IronDev.Core.Builder.PatchValidationResult
            {
                AllValid = true,
                Summary  = "Validation passed: 1 file change ready to apply."
            },
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
                }],
                StandardsCompliance = "Stub compliance: all rules satisfied."
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
/// <summary>
/// Minimal stub IDraftTicketService — returns a deterministic DraftTicket.
/// Used by ViewModel tests; no LLM called.
/// </summary>
internal sealed class StubDraftTicketService : IDraftTicketService
{
    public string LastTitle { get; private set; } = string.Empty;

    public Task<DraftTicket> GenerateDraftAsync(
        int    projectId,
        string projectName,
        string proposedTitle,
        string messageText,
        string? linkedFilePaths,
        string? linkedSymbols,
        long?   sessionId = null,
        CancellationToken ct = default)
    {
        LastTitle = proposedTitle;
        return Task.FromResult(new DraftTicket
        {
            Title              = string.IsNullOrWhiteSpace(proposedTitle) ? "Stub Ticket" : proposedTitle,
            TicketType         = "Task",
            Priority           = "Medium",
            Status             = "Draft",
            Summary            = messageText.Length > 100 ? messageText[..100] : messageText,
            Background         = "Stub requirements.",
            AcceptanceCriteria = "- Stub AC.",
            LinkedFilePaths    = linkedFilePaths,
            LinkedSymbols      = linkedSymbols,
            UnitTests          = "Stub unit tests.",
            IntegrationTests   = "Stub integration tests.",
            ManualTests        = "Stub manual tests.",
            RegressionTests    = "Stub regression tests.",
            BuildValidation    = "dotnet build",
            IsGenerated        = true,
            GenerationNote     = "Stub draft service."
        });
    }

    public Task<DraftTicket> RegenerateTestsAsync(
        int         projectId,
        DraftTicket current,
        CancellationToken ct = default)
        => Task.FromResult(new DraftTicket
        {
            // Preserve non-test fields
            Title               = current.Title,
            TicketType          = current.TicketType,
            Priority            = current.Priority,
            Status              = current.Status,
            Summary             = current.Summary,
            Background          = current.Background,
            AcceptanceCriteria  = current.AcceptanceCriteria,
            LinkedFilePaths     = current.LinkedFilePaths,
            LinkedSymbols       = current.LinkedSymbols,
            ImplementationPlan  = current.ImplementationPlan,
            SourceChatSessionId = current.SourceChatSessionId,
            SourceMessageId     = current.SourceMessageId,
            SourceMessageText   = current.SourceMessageText,
            // New test fields
            UnitTests        = current.UnitTests + " [regenerated]",
            IntegrationTests = current.IntegrationTests + " [regenerated]",
            ManualTests      = current.ManualTests,
            RegressionTests  = current.RegressionTests,
            BuildValidation  = current.BuildValidation,
            IsGenerated      = true,
            GenerationNote   = "Stub tests regenerated."
        });

    public Task<DraftTicket> GeneratePlanAsync(
        int         projectId,
        DraftTicket current,
        CancellationToken ct = default)
        => Task.FromResult(new DraftTicket
        {
            // Preserve fields
            Title               = current.Title,
            TicketType          = current.TicketType,
            Priority            = current.Priority,
            Status              = current.Status,
            Summary             = current.Summary,
            Background          = current.Background,
            AcceptanceCriteria  = current.AcceptanceCriteria,
            LinkedFilePaths     = current.LinkedFilePaths,
            LinkedSymbols       = current.LinkedSymbols,
            SourceChatSessionId = current.SourceChatSessionId,
            SourceMessageId     = current.SourceMessageId,
            SourceMessageText   = current.SourceMessageText,
            UnitTests           = current.UnitTests,
            IntegrationTests    = current.IntegrationTests,
            ManualTests         = current.ManualTests,
            RegressionTests     = current.RegressionTests,
            BuildValidation     = current.BuildValidation,

            // New plan
            ImplementationPlan = "- Step 1: Fix the thing.\n- Step 2: Test the thing.",
            IsGenerated        = true,
            GenerationNote     = "Stub implementation plan generated."
        });
}

/// <summary>
/// ViewModel unit tests for Build Ticket MVP — Phases 1 and 2.
/// No LLM, no Weaviate, no database, no file writes.
/// </summary>
internal sealed class NullLlmTraceService : ILlmTraceService
{
    public event EventHandler<LlmTraceEntry>? TraceAdded;
    public bool IsTracingEnabled { get; set; } = true;
    public void AddTrace(LlmTraceEntry entry) { }
    public void Clear() { }
    public string ExportAll() => string.Empty;
    public string ExportTrace(LlmTraceEntry entry) => string.Empty;
    public IReadOnlyList<LlmTraceEntry> GetRecentTraces(int take = 100) => [];
    public string Redact(string text) => text;
}
[TestClass]
public sealed class TicketsBuildScaffoldingTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static TicketsWorkspaceViewModel CreateVm(ITicketBuildOrchestrator? orchestrator = null)
        => new(null!, null!, orchestrator ?? new StubOrchestrator(), new StubDraftTicketService(), null!);

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

        StringAssert.Contains(vm.BuildStatusMessage, "Validation passed");
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

        public Task<bool> ArchiveTicketAsync(long ticketId, CancellationToken ct = default)
            => Task.FromResult(true);
    }

    private sealed class StubMemory : IronDev.Services.IProjectMemoryService
    {
        private readonly IronDev.Data.Models.ProjectImplementationPlan? _plan;
        private readonly IReadOnlyList<IronDev.Data.Models.ProjectDecision> _decisions;
        private readonly IReadOnlyList<IronDev.Data.Models.ProjectRule> _rules;

        public StubMemory(
            IronDev.Data.Models.ProjectImplementationPlan? plan = null,
            IReadOnlyList<IronDev.Data.Models.ProjectDecision>? decisions = null,
            IReadOnlyList<IronDev.Data.Models.ProjectRule>? rules = null)
        {
            _plan      = plan;
            _decisions = decisions ?? [];
            _rules     = rules ?? [];
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

        public Task<IReadOnlyList<ProjectRule>> GetProjectRulesAsync(int projectId, CancellationToken cancellationToken = default)
            => Task.FromResult(_rules);
        public Task<long> SaveProjectRuleAsync(ProjectRule rule, CancellationToken cancellationToken = default)
            => Task.FromResult(1L);
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
        IReadOnlyList<IronDev.Data.Models.ProjectDecision>?    decisions = null,
        IReadOnlyList<IronDev.Data.Models.ProjectRule>?        rules     = null)
        => new(
            new StubTickets(ticket  ?? MakeTicket()),
            new StubProject(MakeProject()),
            new StubMemory(plan, decisions, rules),
            new StubProfile());

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

    [TestMethod]
    [Description("BuilderContextService includes relevant project rules in context.")]
    public async Task AssembleContextAsync_WithRules_PopulatesStandards()
    {
        var rules = (IReadOnlyList<IronDev.Data.Models.ProjectRule>)
        [
            new IronDev.Data.Models.ProjectRule
            {
                Id = 1, Name = "SQL Rule", AppliesTo = "Both", EnforcementLevel = "Required", Description = "Use SQL"
            },
            new IronDev.Data.Models.ProjectRule
            {
                Id = 2, Name = "Build Rule", AppliesTo = "Build", EnforcementLevel = "Blocking", Description = "No warnings"
            },
            new IronDev.Data.Models.ProjectRule
            {
                Id = 3, Name = "Ticket Rule", AppliesTo = "Ticket", EnforcementLevel = "Advisory", Description = "Add labels"
            }
        ];
        var svc = MakeSvc(rules: rules);
        var ctx = await svc.AssembleContextAsync(1, 1);

        // Only Both and Build rules should be included
        Assert.AreEqual(2, ctx.Standards.Count);
        Assert.IsTrue(ctx.Standards.Any(r => r.Name == "SQL Rule"));
        Assert.IsTrue(ctx.Standards.Any(r => r.Name == "Build Rule"));
        Assert.IsFalse(ctx.Standards.Any(r => r.Name == "Ticket Rule"));
        
        // Blocking should be first
        Assert.AreEqual("Build Rule", ctx.Standards[0].Name);
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
        public Task<string> GetResponseAsync(string prompt, System.Threading.CancellationToken ct = default) => Task.FromResult(_response);
    }

    private sealed class ThrowingLlm : IronDev.Core.ILLMService
    {
        private readonly string _message;
        public ThrowingLlm(string message = "Network error") => _message = message;
        public Task<string> GetResponseAsync(string prompt, System.Threading.CancellationToken ct = default) =>
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
          "summary": "Replace 'Conversation History' with 'Conversations'.",
          "rationale": "Low — XAML layout only.",
          "changes": [
            {
              "filePath": "Views\\ChatWorkspaceView.xaml",
              "description": "Header text clips.",
              "diff": "@@ -1,1 +1,1 @@\n-Conversation History\n+Conversations",
              "fullContentAfter": "new content"
            }
          ]
        }
        """;

    // ── Parsing tests ─────────────────────────────────────────────────────────

    [TestMethod]
    [Description("CodeChangeProposalService parses valid JSON into CodeChangeProposal.")]
    public async Task ParsesValidJson_ReturnsProposal()
    {
        var svc = new IronDev.Infrastructure.Builder.CodeChangeProposalService(new FakeLlm(ValidJson()), new NullLlmTraceService());
        var proposal = await svc.GenerateProposalAsync(MakeContext());

        Assert.AreEqual("Replace 'Conversation History' with 'Conversations'.", proposal.Summary);
        Assert.HasCount(1, proposal.FileChanges);
        Assert.AreEqual(@"Views\ChatWorkspaceView.xaml", proposal.FileChanges[0].FilePath);
        Assert.AreEqual("Generated in proposal-only mode.", proposal.RiskNotes);
        Assert.AreEqual("Low — XAML layout only.", proposal.Rationale);
    }

    [TestMethod]
    [Description("CodeChangeProposalService strips markdown fences before parsing.")]
    public async Task StripsFences_BeforeParsing()
    {
        var wrapped = "```json\n" + ValidJson() + "\n```";
        var svc     = new IronDev.Infrastructure.Builder.CodeChangeProposalService(new FakeLlm(wrapped), new NullLlmTraceService());
        var proposal = await svc.GenerateProposalAsync(MakeContext());

        Assert.AreEqual("Replace 'Conversation History' with 'Conversations'.", proposal.Summary);
    }

    [TestMethod]
    [Description("CodeChangeProposalService rejects invalid JSON with clear exception.")]
    public async Task InvalidJson_ThrowsWithClearMessage()
    {
        var svc = new IronDev.Infrastructure.Builder.CodeChangeProposalService(
            new FakeLlm("this is not json at all"), new NullLlmTraceService());

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
              "summary": "Nothing to change.",
              "rationale": "N/A",
              "changes": []
            }
            """;
        var svc = new IronDev.Infrastructure.Builder.CodeChangeProposalService(new FakeLlm(json), new NullLlmTraceService());
        var proposal = await svc.GenerateProposalAsync(MakeContext());

        Assert.HasCount(0, proposal.FileChanges);
        Assert.AreEqual("Nothing to change.", proposal.Summary);
    }

    [TestMethod]
    [Description("CodeChangeProposalService handles LLM failure with clear exception.")]
    public async Task LlmThrows_PropagatesWithClearMessage()
    {
        var svc = new IronDev.Infrastructure.Builder.CodeChangeProposalService(
            new ThrowingLlm("Simulated timeout"), new NullLlmTraceService());

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
            { "summary": "ok", "rationale": "low", "changes": [] }
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
        IronDev.Core.Interfaces.ICodeChangeProposalService? proposalSvc = null,
        IronDev.Core.Interfaces.ICodePatchService?          patchSvc    = null)
        => new(ctxSvc ?? new StubContextSvc(), proposalSvc ?? new StubProposalSvc(),
               patchSvc ?? new IronDev.Infrastructure.Builder.CodePatchService());

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
        var vm   = new TicketsWorkspaceViewModel(null!, null!, orch, new StubDraftTicketService(), null!);

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
        StringAssert.Contains(vm.BuildStatusMessage, "Validation");
    }

    [TestMethod]
    [Description("ViewModel: LLM failure sets BuildStatusMessage to 'AI proposal failed:'.")]
    public async Task ViewModel_LlmFailure_SetsBuildFailedMessage()
    {
        var orch = MakeOrchestrator(proposalSvc: new FailingProposalSvc());
        var vm   = new TicketsWorkspaceViewModel(null!, null!, orch, new StubDraftTicketService(), null!);

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
        var vm   = new TicketsWorkspaceViewModel(null!, null!, orch, new StubDraftTicketService(), null!);

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
