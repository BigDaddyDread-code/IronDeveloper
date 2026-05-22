using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IronDev.Core.Builder;
using IronDev.Core.Interfaces;
using IronDev.Core.KnowledgeCompiler;
using IronDev.Core.Workflow;
using IronDev.Data.Models;
using IronDev.Infrastructure.Workflow;
using IronDev.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class TicketBuildWorkflowTests
{
    [TestMethod]
    public async Task StartAsync_ShouldStopAtCodeApprovalWithSemanticContextAndProposal()
    {
        // Arrange
        var ticket = new ProjectTicket
        {
            Id = 42,
            ProjectId = 7,
            Title = "Add semantic memory to build planner",
            Summary = "Planner must retrieve Knowledge Compiler memory before proposing code.",
            Problem = "Build prompts drift without authority-aware project context.",
            AcceptanceCriteria = "Plan contains semantic memory context and pauses for approval.",
            TechnicalNotes = "Inspect IronDev.Core/KnowledgeCompiler/ISemanticMemoryService.cs",
            Status = "Draft"
        };

        var orchestrator = CreateOrchestrator(ticket, new SemanticWorkflowContext
        {
            ProjectId = 7,
            Consumer = "TicketBuildWorkflow",
            QueryText = ticket.Title,
            PromptContextMarkdown = "## Retrieved Project Memory\n\n### 1. SQL is canonical",
            Items =
            [
                new SemanticWorkflowMemoryItem
                {
                    ArtefactId = Guid.NewGuid(),
                    ChunkId = Guid.NewGuid(),
                    Title = "SQL is canonical",
                    ArtefactType = "Decision",
                    AuthorityLevel = "CommittedDecision",
                    Snippet = "SQL owns project memory; Weaviate is rebuildable.",
                    FinalScore = 1.2,
                    MatchReason = "High authority project decision."
                }
            ]
        });

        // Act
        var result = await orchestrator.StartAsync(new TicketBuildWorkflowRequest
        {
            ProjectId = 7,
            TicketId = 42
        });

        // Assert
        Assert.AreEqual(TicketBuildWorkflowStatus.AwaitingCodeApproval, result.Status);
        Assert.AreEqual(TicketBuildWorkflowNodes.RequestCodeApproval, result.CurrentNode);
        Assert.IsTrue(result.RequiresHumanApproval);
        Assert.IsNotNull(result.State.ImplementationPlanMarkdown);
        StringAssert.Contains(result.State.ImplementationPlanMarkdown!, "Retrieved Project Memory");
        StringAssert.Contains(result.State.ImplementationPlanMarkdown!, "SQL is canonical");
        Assert.IsNotNull(result.State.CodeProposal);
        Assert.AreEqual(1, result.State.CodeProposal!.FileChanges.Count);
        Assert.IsNotNull(result.State.PatchValidation);
        Assert.AreEqual(3, result.State.ToolCalls.Count);
        Assert.AreEqual(1, result.State.KnowledgeMemoryItems.Count);
        Assert.IsTrue(result.State.TraceMessages.Any(x => x.Contains("Knowledge context compiled", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(result.State.TraceMessages.Any(x => x.Contains("No files have been changed", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task StartAsync_ShouldFailWhenTicketProjectDoesNotMatch()
    {
        // Arrange
        var ticket = new ProjectTicket
        {
            Id = 42,
            ProjectId = 99,
            Title = "Wrong project ticket"
        };

        var orchestrator = CreateOrchestrator(ticket, new SemanticWorkflowContext());

        // Act
        var result = await orchestrator.StartAsync(new TicketBuildWorkflowRequest
        {
            ProjectId = 7,
            TicketId = 42
        });

        // Assert
        Assert.AreEqual(TicketBuildWorkflowStatus.Failed, result.Status);
        Assert.AreEqual(TicketBuildWorkflowNodes.Failed, result.CurrentNode);
        StringAssert.Contains(result.Message!, "belongs to project 99");
    }

    private static TicketBuildWorkflowOrchestrator CreateOrchestrator(
        ProjectTicket ticket,
        SemanticWorkflowContext memoryContext)
    {
        IWorkflowNode<TicketBuildWorkflowState>[] nodes =
        [
            new LoadTicketNode(new StubTicketService(ticket)),
            new CompileKnowledgeContextNode(new StubSemanticWorkflowMemoryNode(memoryContext)),
            new CreateImplementationPlanNode(),
            new ProposeCodeChangesNode(
                new StubBuilderContextService(ticket),
                new StubCodeChangeProposalService(),
                new StubCodePatchService()),
            new RequestCodeApprovalNode(),
            new RequestPlanApprovalNode()
        ];

        return new TicketBuildWorkflowOrchestrator(nodes);
    }

    private sealed class StubTicketService : ITicketService
    {
        private readonly ProjectTicket _ticket;

        public StubTicketService(ProjectTicket ticket)
            => _ticket = ticket;

        public Task<long> SaveTicketAsync(ProjectTicket ticket, CancellationToken cancellationToken = default)
            => Task.FromResult(ticket.Id);

        public Task<IReadOnlyList<ProjectTicket>> GetRecentTicketsAsync(int projectId, int take = 10, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ProjectTicket>>([_ticket]);

        public Task<ProjectTicket?> GetTicketByIdAsync(long ticketId, CancellationToken cancellationToken = default)
            => Task.FromResult<ProjectTicket?>(_ticket.Id == ticketId ? _ticket : null);

        public Task<bool> ArchiveTicketAsync(long ticketId, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }

    private sealed class StubSemanticWorkflowMemoryNode : ISemanticWorkflowMemoryNode
    {
        private readonly SemanticWorkflowContext _context;

        public StubSemanticWorkflowMemoryNode(SemanticWorkflowContext context)
            => _context = context;

        public Task<SemanticWorkflowContext> BuildContextAsync(
            SemanticWorkflowNodeRequest request,
            CancellationToken ct = default)
            => Task.FromResult(_context with
            {
                ProjectId = request.ProjectId,
                Consumer = request.Consumer
            });
    }

    private sealed class StubBuilderContextService : IBuilderContextService
    {
        private readonly ProjectTicket _ticket;

        public StubBuilderContextService(ProjectTicket ticket)
            => _ticket = ticket;

        public Task<TicketBuildContext> AssembleContextAsync(
            int projectId,
            long ticketId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new TicketBuildContext
            {
                ProjectId = projectId,
                TicketId = ticketId,
                ProjectName = "IronDev",
                ProjectPath = Environment.CurrentDirectory,
                TicketTitle = _ticket.Title,
                TicketSummary = _ticket.Summary ?? string.Empty,
                TicketProblem = _ticket.Problem,
                TicketAcceptanceCriteria = _ticket.AcceptanceCriteria,
                TicketImplementationNotes = _ticket.TechnicalNotes,
                AffectedFiles = ["IronDev.Core/Workflow/TicketBuildWorkflowModels.cs"]
            });
    }

    private sealed class StubCodeChangeProposalService : ICodeChangeProposalService
    {
        public Task<CodeChangeProposal> GenerateProposalAsync(
            TicketBuildContext context,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new CodeChangeProposal
            {
                TicketId = context.TicketId,
                Summary = "Add workflow support.",
                Rationale = "Ticket requires Build Agent workflow state.",
                RiskNotes = "Proposal only.",
                TestPlan = "Run workflow tests.",
                FileChanges =
                [
                    new FileChangeProposal
                    {
                        FilePath = "IronDev.Core/Workflow/TicketBuildWorkflowModels.cs",
                        ChangeReason = "Add workflow state.",
                        Patch = "--- a/IronDev.Core/Workflow/TicketBuildWorkflowModels.cs\n+++ b/IronDev.Core/Workflow/TicketBuildWorkflowModels.cs\n@@ ..."
                    }
                ]
            });
    }

    private sealed class StubCodePatchService : ICodePatchService
    {
        public Task<GitStatus> GetGitStatusAsync(string projectPath, CancellationToken cancellationToken = default)
            => Task.FromResult(new GitStatus());

        public Task<PatchValidationResult> DryRunValidateAsync(
            string projectPath,
            IReadOnlyList<FileChangeProposal> changes,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new PatchValidationResult
            {
                AllValid = true,
                Summary = $"Validation passed: {changes.Count} proposed change(s)."
            });

        public Task<PatchApplyResult> ApplyPatchesAsync(
            string projectPath,
            IReadOnlyList<FileChangeProposal> changes,
            CancellationToken cancellationToken = default)
            => Task.FromResult(PatchApplyResult.Failure("Not used by this workflow slice."));
    }
}
