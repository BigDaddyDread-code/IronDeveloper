using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IronDev.Core.Builder;
using IronDev.Core.Interfaces;
using IronDev.Core.KnowledgeCompiler;
using IronDev.Core.Workflow;
using IronDev.Data.Models;
using IronDev.Services;

namespace IronDev.Infrastructure.Workflow;

public sealed class LoadTicketNode : IWorkflowNode<TicketBuildWorkflowState>
{
    private readonly ITicketService _ticketService;

    public LoadTicketNode(ITicketService ticketService)
        => _ticketService = ticketService;

    public string Name => TicketBuildWorkflowNodes.LoadTicket;

    public async Task<WorkflowNodeResult<TicketBuildWorkflowState>> ExecuteAsync(
        TicketBuildWorkflowState state,
        CancellationToken cancellationToken = default)
    {
        var ticket = await _ticketService.GetTicketByIdAsync(state.TicketId, cancellationToken);
        if (ticket == null)
            return Failed(state, $"Ticket {state.TicketId} was not found.");

        if (ticket.ProjectId != state.ProjectId)
            return Failed(state, $"Ticket {state.TicketId} belongs to project {ticket.ProjectId}, not project {state.ProjectId}.");

        state.TicketTitle = ticket.Title;
        state.TicketSummary = ticket.Summary;
        state.TicketProblem = ticket.Problem;
        state.TicketAcceptanceCriteria = ticket.AcceptanceCriteria;
        state.TicketTechnicalNotes = ticket.TechnicalNotes;
        state.TicketDescription = BuildTicketDescription(ticket);
        state.AffectedFiles = ResolveAffectedFiles(ticket).ToList();
        state.CurrentNode = Name;
        state.TraceMessages.Add($"Ticket loaded: {ticket.Title}");

        return new WorkflowNodeResult<TicketBuildWorkflowState>
        {
            State = state,
            NextNode = TicketBuildWorkflowNodes.CompileKnowledgeContext,
            Message = "Ticket loaded."
        };
    }

    private static WorkflowNodeResult<TicketBuildWorkflowState> Failed(TicketBuildWorkflowState state, string message)
    {
        state.Status = TicketBuildWorkflowStatus.Failed;
        state.CurrentNode = TicketBuildWorkflowNodes.Failed;
        state.TraceMessages.Add(message);

        return new WorkflowNodeResult<TicketBuildWorkflowState>
        {
            State = state,
            NextNode = TicketBuildWorkflowNodes.Failed,
            IsTerminal = true,
            Message = message
        };
    }

    private static string BuildTicketDescription(ProjectTicket ticket)
    {
        var sb = new StringBuilder();
        Append(sb, "Title", ticket.Title);
        Append(sb, "Summary", ticket.Summary);
        Append(sb, "Problem", ticket.Problem);
        Append(sb, "Acceptance Criteria", ticket.AcceptanceCriteria);
        Append(sb, "Technical Notes", ticket.TechnicalNotes);
        return sb.ToString().Trim();
    }

    private static void Append(StringBuilder sb, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            sb.AppendLine($"{label}: {value.Trim()}");
    }

    private static IReadOnlyList<string> ResolveAffectedFiles(ProjectTicket ticket)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in new[] { ticket.LinkedFilePaths, ticket.TechnicalNotes })
        {
            if (string.IsNullOrWhiteSpace(source)) continue;
            foreach (var part in source.Split(['\n', '\r', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (part.Contains('\\') || part.Contains('/') || part.Contains('.'))
                    result.Add(part);
            }
        }

        return result.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }
}

public sealed class CompileKnowledgeContextNode : IWorkflowNode<TicketBuildWorkflowState>
{
    private readonly ISemanticWorkflowMemoryNode _memoryNode;

    public CompileKnowledgeContextNode(ISemanticWorkflowMemoryNode memoryNode)
        => _memoryNode = memoryNode;

    public string Name => TicketBuildWorkflowNodes.CompileKnowledgeContext;

    public async Task<WorkflowNodeResult<TicketBuildWorkflowState>> ExecuteAsync(
        TicketBuildWorkflowState state,
        CancellationToken cancellationToken = default)
    {
        var memory = await _memoryNode.BuildContextAsync(new SemanticWorkflowNodeRequest
        {
            ProjectId = state.ProjectId,
            Consumer = "TicketBuildWorkflow",
            Goal = "Compile authority-aware context for an implementation plan.",
            UserRequest = state.TicketDescription ?? state.TicketTitle ?? string.Empty,
            TicketId = state.TicketId,
            PreferredArtefactTypes = ["Decision", "Architecture", "Requirement", "Ticket", "CodeSummary", "TestingCompanionReport"],
            Limit = 8
        }, cancellationToken);

        state.KnowledgeContextMarkdown = memory.PromptContextMarkdown;
        state.KnowledgeMemoryItems = memory.Items.ToList();
        state.KnowledgeArtefactIds = memory.Items.Select(x => x.ArtefactId).Distinct().ToList();
        state.CurrentNode = Name;
        state.TraceMessages.Add($"Knowledge context compiled: {memory.Items.Count} memory item(s).");
        foreach (var warning in memory.Warnings)
            state.TraceMessages.Add($"Knowledge context warning: {warning}");

        return new WorkflowNodeResult<TicketBuildWorkflowState>
        {
            State = state,
            NextNode = TicketBuildWorkflowNodes.CreateImplementationPlan,
            Message = "Knowledge context compiled."
        };
    }
}

public sealed class CreateImplementationPlanNode : IWorkflowNode<TicketBuildWorkflowState>
{
    public string Name => TicketBuildWorkflowNodes.CreateImplementationPlan;

    public Task<WorkflowNodeResult<TicketBuildWorkflowState>> ExecuteAsync(
        TicketBuildWorkflowState state,
        CancellationToken cancellationToken = default)
    {
        state.ImplementationPlanMarkdown = BuildPlan(state);
        state.CurrentNode = Name;
        state.TraceMessages.Add("Implementation plan generated.");

        return Task.FromResult(new WorkflowNodeResult<TicketBuildWorkflowState>
        {
            State = state,
            NextNode = TicketBuildWorkflowNodes.ProposeCodeChanges,
            Message = "Implementation plan generated."
        });
    }

    private static string BuildPlan(TicketBuildWorkflowState state)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Implementation Plan: {state.TicketTitle}");
        sb.AppendLine();
        AppendSection(sb, "Ticket", state.TicketDescription);
        AppendSection(sb, "Retrieved Knowledge Context", state.KnowledgeContextMarkdown);

        sb.AppendLine("## Proposed Steps");
        sb.AppendLine("1. Review the ticket details and retrieved project memory.");
        sb.AppendLine("2. Confirm affected components and files before generating code changes.");
        sb.AppendLine("3. Produce a proposal-first code change after approval.");
        sb.AppendLine("4. Run the configured build and tests after patch approval.");
        sb.AppendLine();

        sb.AppendLine("## Affected Files / Components");
        if (state.AffectedFiles.Count == 0)
        {
            sb.AppendLine("- To be confirmed from semantic/code context.");
        }
        else
        {
            foreach (var file in state.AffectedFiles)
                sb.AppendLine($"- {file}");
        }
        sb.AppendLine();

        sb.AppendLine("## Risks");
        sb.AppendLine("- Do not apply code automatically before human approval.");
        sb.AppendLine("- Keep changes scoped to the ticket and cited memory.");
        sb.AppendLine();

        sb.AppendLine("## Test Strategy");
        sb.AppendLine("- Run the project build command.");
        sb.AppendLine("- Run relevant automated tests where configured.");
        sb.AppendLine("- Use Testing Companion for manual dogfood verification if UI behavior changes.");

        return sb.ToString().Trim();
    }

    private static void AppendSection(StringBuilder sb, string title, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        sb.AppendLine($"## {title}");
        sb.AppendLine(value.Trim());
        sb.AppendLine();
    }
}

public sealed class ProposeCodeChangesNode : IWorkflowNode<TicketBuildWorkflowState>
{
    private readonly IBuilderContextService _contextService;
    private readonly ICodeChangeProposalService _proposalService;
    private readonly ICodePatchService _patchService;

    public ProposeCodeChangesNode(
        IBuilderContextService contextService,
        ICodeChangeProposalService proposalService,
        ICodePatchService patchService)
    {
        _contextService = contextService;
        _proposalService = proposalService;
        _patchService = patchService;
    }

    public string Name => TicketBuildWorkflowNodes.ProposeCodeChanges;

    public async Task<WorkflowNodeResult<TicketBuildWorkflowState>> ExecuteAsync(
        TicketBuildWorkflowState state,
        CancellationToken cancellationToken = default)
    {
        var context = await RunToolAsync(
            state,
            "AssembleBuilderContext",
            async () => await _contextService.AssembleContextAsync(state.ProjectId, state.TicketId, cancellationToken));

        if (!string.IsNullOrWhiteSpace(state.KnowledgeContextMarkdown))
        {
            var decisions = context.Decisions.ToList();
            decisions.Add("--- SEMANTIC MEMORY USED BY BUILD AGENT ---");
            decisions.Add(state.KnowledgeContextMarkdown);
            context.Decisions = decisions.AsReadOnly();
        }

        if (!string.IsNullOrWhiteSpace(state.ImplementationPlanMarkdown))
        {
            context.PlanTitle ??= $"Workflow plan for ticket {state.TicketId}";
            context.PlanGoal ??= state.TicketTitle;
            context.PlanSteps ??= state.ImplementationPlanMarkdown;
        }

        var proposal = await RunToolAsync(
            state,
            "ProposePatch",
            async () => await _proposalService.GenerateProposalAsync(context, cancellationToken));
        proposal.TicketId = state.TicketId;

        var validation = await RunToolAsync(
            state,
            "DryRunPatchValidation",
            async () => await _patchService.DryRunValidateAsync(context.ProjectPath, proposal.FileChanges, cancellationToken));

        state.CodeProposal = proposal;
        state.PatchValidation = validation;
        state.GeneratedPatch = string.Join(
            Environment.NewLine + Environment.NewLine,
            proposal.FileChanges
                .Where(x => !string.IsNullOrWhiteSpace(x.Patch))
                .Select(x => $"--- {x.FilePath} ---{Environment.NewLine}{x.Patch}"));
        state.CurrentNode = Name;
        state.TraceMessages.Add($"Code proposal generated: {proposal.FileChanges.Count} file change(s).");
        state.TraceMessages.Add(validation.Summary);

        return new WorkflowNodeResult<TicketBuildWorkflowState>
        {
            State = state,
            NextNode = TicketBuildWorkflowNodes.RequestCodeApproval,
            Message = "Code proposal generated and dry-run validated."
        };
    }

    private static async Task<T> RunToolAsync<T>(
        TicketBuildWorkflowState state,
        string toolName,
        Func<Task<T>> action)
    {
        var started = DateTime.UtcNow;
        try
        {
            var result = await action();
            state.ToolCalls.Add(new WorkflowToolCall
            {
                ToolName = toolName,
                NodeName = TicketBuildWorkflowNodes.ProposeCodeChanges,
                Status = "Completed",
                StartedUtc = started,
                CompletedUtc = DateTime.UtcNow
            });
            return result;
        }
        catch (Exception ex)
        {
            state.ToolCalls.Add(new WorkflowToolCall
            {
                ToolName = toolName,
                NodeName = TicketBuildWorkflowNodes.ProposeCodeChanges,
                Status = "Failed",
                Summary = ex.Message,
                StartedUtc = started,
                CompletedUtc = DateTime.UtcNow
            });
            throw;
        }
    }
}

public sealed class RequestPlanApprovalNode : IWorkflowNode<TicketBuildWorkflowState>
{
    public string Name => TicketBuildWorkflowNodes.RequestPlanApproval;

    public Task<WorkflowNodeResult<TicketBuildWorkflowState>> ExecuteAsync(
        TicketBuildWorkflowState state,
        CancellationToken cancellationToken = default)
    {
        state.CurrentNode = Name;
        state.Status = TicketBuildWorkflowStatus.AwaitingPlanApproval;
        state.RequiresHumanApproval = true;
        state.PlanApprovalStatus = WorkflowApprovalStatus.Pending;
        state.TraceMessages.Add("Workflow paused for implementation plan approval.");

        return Task.FromResult(new WorkflowNodeResult<TicketBuildWorkflowState>
        {
            State = state,
            NextNode = TicketBuildWorkflowNodes.RequestPlanApproval,
            RequiresHumanApproval = true,
            IsTerminal = true,
            Message = "Implementation plan is ready for human approval."
        });
    }
}

public sealed class RequestCodeApprovalNode : IWorkflowNode<TicketBuildWorkflowState>
{
    public string Name => TicketBuildWorkflowNodes.RequestCodeApproval;

    public Task<WorkflowNodeResult<TicketBuildWorkflowState>> ExecuteAsync(
        TicketBuildWorkflowState state,
        CancellationToken cancellationToken = default)
    {
        state.CurrentNode = Name;
        state.Status = TicketBuildWorkflowStatus.AwaitingCodeApproval;
        state.RequiresHumanApproval = true;
        state.CodeApprovalStatus = WorkflowApprovalStatus.Pending;
        state.TraceMessages.Add("Workflow paused for code proposal approval. No files have been changed.");

        return Task.FromResult(new WorkflowNodeResult<TicketBuildWorkflowState>
        {
            State = state,
            NextNode = TicketBuildWorkflowNodes.RequestCodeApproval,
            RequiresHumanApproval = true,
            IsTerminal = true,
            Message = "Code proposal is ready for human approval."
        });
    }
}
