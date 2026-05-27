using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IronDev.Core.RunReports;
using IronDev.Core.Workflow;

namespace IronDev.Infrastructure.Workflow;

public sealed class TicketBuildWorkflowOrchestrator : ITicketBuildWorkflowOrchestrator
{
    private readonly IReadOnlyDictionary<string, IWorkflowNode<TicketBuildWorkflowState>> _nodes;
    private readonly IRunEventStore _events;

    public TicketBuildWorkflowOrchestrator(
        IEnumerable<IWorkflowNode<TicketBuildWorkflowState>> nodes,
        IRunEventStore? events = null)
    {
        var map = new Dictionary<string, IWorkflowNode<TicketBuildWorkflowState>>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodes)
            map[node.Name] = node;

        _nodes = map;
        _events = events ?? NullRunEventStore.Instance;
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

        await PublishAsync(
            state,
            "RunStarted",
            $"Ticket build run started for ticket {state.TicketId}.",
            cancellationToken,
            new Dictionary<string, string>
            {
                ["status"] = state.Status.ToString()
            });
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
                await PublishAsync(state, "Error", message, cancellationToken);
                break;
            }

            state.TraceMessages.Add($"Entering node: {node.Name}");
            await PublishAsync(state, "StepStarted", $"Entering node: {node.Name}", cancellationToken, new Dictionary<string, string>
            {
                ["node"] = node.Name
            });

            WorkflowNodeResult<TicketBuildWorkflowState> result;
            try
            {
                result = await node.ExecuteAsync(state, cancellationToken);
            }
            catch (Exception ex)
            {
                state.Status = TicketBuildWorkflowStatus.Failed;
                message = ex.Message;
                state.TraceMessages.Add(ex.Message);
                await PublishAsync(state, "Error", ex.Message, cancellationToken, new Dictionary<string, string>
                {
                    ["node"] = node.Name,
                    ["exceptionType"] = ex.GetType().Name
                });
                break;
            }

            state = result.State;
            message = result.Message;
            await PublishToolEventsAsync(state, node.Name, cancellationToken);
            await PublishAsync(state, "StepCompleted", result.Message ?? $"Completed node: {node.Name}", cancellationToken, new Dictionary<string, string>
            {
                ["node"] = node.Name,
                ["nextNode"] = result.NextNode,
                ["status"] = state.Status.ToString()
            });

            if (result.RequiresHumanApproval)
            {
                state.RequiresHumanApproval = true;
                state.Status = state.CurrentNode == TicketBuildWorkflowNodes.RequestCodeApproval
                    ? TicketBuildWorkflowStatus.AwaitingCodeApproval
                    : TicketBuildWorkflowStatus.AwaitingPlanApproval;
                await PublishAsync(state, "ApprovalRequired", message ?? "Workflow requires human approval.", cancellationToken, new Dictionary<string, string>
                {
                    ["node"] = state.CurrentNode,
                    ["status"] = state.Status.ToString()
                });
                break;
            }

            if (result.IsTerminal)
                break;

            state.CurrentNode = result.NextNode;
        }

        if (!state.RequiresHumanApproval)
        {
            await PublishAsync(
                state,
                state.Status == TicketBuildWorkflowStatus.Failed ? "RunFailed" : "RunCompleted",
                message ?? $"Ticket build run finished with status {state.Status}.",
                cancellationToken,
                new Dictionary<string, string>
                {
                    ["status"] = state.Status.ToString(),
                    ["currentNode"] = state.CurrentNode
                });
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

    private async Task PublishToolEventsAsync(
        TicketBuildWorkflowState state,
        string nodeName,
        CancellationToken cancellationToken)
    {
        foreach (var toolCall in state.ToolCalls)
        {
            if (!string.Equals(toolCall.NodeName, nodeName, StringComparison.OrdinalIgnoreCase))
                continue;

            await PublishAsync(state, "ToolCallCompleted", toolCall.Summary ?? $"Tool call {toolCall.ToolName} {toolCall.Status}.", cancellationToken, new Dictionary<string, string>
            {
                ["node"] = toolCall.NodeName,
                ["toolName"] = toolCall.ToolName,
                ["status"] = toolCall.Status
            });
        }
    }

    private Task PublishAsync(
        TicketBuildWorkflowState state,
        string eventType,
        string message,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? payload = null)
    {
        var mergedPayload = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["projectId"] = state.ProjectId.ToString(),
            ["ticketId"] = state.TicketId.ToString(),
            ["disposableRun"] = "true",
            ["currentNode"] = state.CurrentNode,
            ["status"] = state.Status.ToString()
        };

        if (payload is not null)
        {
            foreach (var pair in payload)
                mergedPayload[pair.Key] = pair.Value;
        }

        return _events.PublishAsync(new RunEventDto
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            RunId = state.WorkflowRunId.ToString("D"),
            EventType = eventType,
            Message = message,
            Payload = mergedPayload
        }, cancellationToken);
    }
}
