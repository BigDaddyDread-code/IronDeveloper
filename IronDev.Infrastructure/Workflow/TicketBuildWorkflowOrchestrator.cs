using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IronDev.Core.Workflow;

namespace IronDev.Infrastructure.Workflow;

public sealed class TicketBuildWorkflowOrchestrator : ITicketBuildWorkflowOrchestrator
{
    private readonly IReadOnlyDictionary<string, IWorkflowNode<TicketBuildWorkflowState>> _nodes;

    public TicketBuildWorkflowOrchestrator(IEnumerable<IWorkflowNode<TicketBuildWorkflowState>> nodes)
    {
        var map = new Dictionary<string, IWorkflowNode<TicketBuildWorkflowState>>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodes)
            map[node.Name] = node;

        _nodes = map;
    }

    public async Task<TicketBuildWorkflowResult> StartAsync(
        TicketBuildWorkflowRequest request,
        CancellationToken cancellationToken = default)
    {
        var state = new TicketBuildWorkflowState
        {
            WorkflowRunId = request.WorkflowRunId ?? Guid.NewGuid(),
            ProjectId = request.ProjectId,
            TicketId = request.TicketId,
            MaxRetries = request.MaxRetries <= 0 ? 3 : request.MaxRetries,
            CurrentNode = TicketBuildWorkflowNodes.LoadTicket,
            Status = TicketBuildWorkflowStatus.Running
        };

        return await RunAsync(state, cancellationToken);
    }

    public Task<TicketBuildWorkflowResult> ResumeAsync(
        Guid workflowRunId,
        CancellationToken cancellationToken = default)
    {
        var state = new TicketBuildWorkflowState
        {
            WorkflowRunId = workflowRunId,
            CurrentNode = TicketBuildWorkflowNodes.Failed,
            Status = TicketBuildWorkflowStatus.Failed
        };

        state.TraceMessages.Add("Resume requires persisted workflow state; persistence is planned for the next slice.");

        return Task.FromResult(new TicketBuildWorkflowResult
        {
            WorkflowRunId = workflowRunId,
            Status = state.Status,
            CurrentNode = state.CurrentNode,
            Message = "Workflow resume is not available until workflow persistence is implemented.",
            State = state
        });
    }

    private async Task<TicketBuildWorkflowResult> RunAsync(
        TicketBuildWorkflowState state,
        CancellationToken cancellationToken)
    {
        string? message = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            if (!_nodes.TryGetValue(state.CurrentNode, out var node))
            {
                state.Status = TicketBuildWorkflowStatus.Failed;
                message = $"Workflow node '{state.CurrentNode}' is not registered.";
                state.TraceMessages.Add(message);
                break;
            }

            state.TraceMessages.Add($"Entering node: {node.Name}");
            var result = await node.ExecuteAsync(state, cancellationToken);
            state = result.State;
            message = result.Message;

            if (result.RequiresHumanApproval)
            {
                state.RequiresHumanApproval = true;
                state.Status = state.CurrentNode == TicketBuildWorkflowNodes.RequestCodeApproval
                    ? TicketBuildWorkflowStatus.AwaitingCodeApproval
                    : TicketBuildWorkflowStatus.AwaitingPlanApproval;
                break;
            }

            if (result.IsTerminal)
                break;

            state.CurrentNode = result.NextNode;
        }

        return new TicketBuildWorkflowResult
        {
            WorkflowRunId = state.WorkflowRunId,
            Status = state.Status,
            CurrentNode = state.CurrentNode,
            RequiresHumanApproval = state.RequiresHumanApproval,
            Message = message,
            State = state
        };
    }
}
