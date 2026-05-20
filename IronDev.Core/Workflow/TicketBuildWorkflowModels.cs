using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IronDev.Core.Builder;
using IronDev.Core.KnowledgeCompiler;

namespace IronDev.Core.Workflow;

public static class TicketBuildWorkflowNodes
{
    public const string LoadTicket = "LoadTicket";
    public const string CompileKnowledgeContext = "CompileKnowledgeContext";
    public const string CreateImplementationPlan = "CreateImplementationPlan";
    public const string RequestPlanApproval = "RequestPlanApproval";
    public const string ProposeCodeChanges = "ProposeCodeChanges";
    public const string RequestCodeApproval = "RequestCodeApproval";
    public const string Complete = "Complete";
    public const string Failed = "Failed";
}

public enum TicketBuildWorkflowStatus
{
    Pending,
    Running,
    AwaitingPlanApproval,
    AwaitingCodeApproval,
    Completed,
    Failed
}

public enum WorkflowApprovalStatus
{
    Pending,
    Approved,
    Rejected,
    ChangesRequested
}

public sealed class TicketBuildWorkflowRequest
{
    public Guid? WorkflowRunId { get; init; }
    public int ProjectId { get; init; }
    public long TicketId { get; init; }
    public int MaxRetries { get; init; } = 3;
}

public sealed class TicketBuildWorkflowResult
{
    public Guid WorkflowRunId { get; init; }
    public TicketBuildWorkflowStatus Status { get; init; }
    public string CurrentNode { get; init; } = string.Empty;
    public bool RequiresHumanApproval { get; init; }
    public string? Message { get; init; }
    public required TicketBuildWorkflowState State { get; init; }
}

public sealed class WorkflowNodeResult<TState>
{
    public required TState State { get; init; }
    public required string NextNode { get; init; }
    public bool RequiresHumanApproval { get; init; }
    public bool IsTerminal { get; init; }
    public string? Message { get; init; }
}

public interface IWorkflowNode<TState>
{
    string Name { get; }

    Task<WorkflowNodeResult<TState>> ExecuteAsync(
        TState state,
        CancellationToken cancellationToken = default);
}

public interface ITicketBuildWorkflowOrchestrator
{
    Task<TicketBuildWorkflowResult> StartAsync(
        TicketBuildWorkflowRequest request,
        CancellationToken cancellationToken = default);

    Task<TicketBuildWorkflowResult> ResumeAsync(
        Guid workflowRunId,
        CancellationToken cancellationToken = default);
}

public sealed class TicketBuildWorkflowState
{
    public Guid WorkflowRunId { get; set; }
    public int ProjectId { get; set; }
    public long TicketId { get; set; }

    public string CurrentNode { get; set; } = TicketBuildWorkflowNodes.LoadTicket;
    public TicketBuildWorkflowStatus Status { get; set; } = TicketBuildWorkflowStatus.Pending;

    public int RetryCount { get; set; }
    public int MaxRetries { get; set; } = 3;
    public bool RequiresHumanApproval { get; set; }
    public WorkflowApprovalStatus PlanApprovalStatus { get; set; } = WorkflowApprovalStatus.Pending;
    public WorkflowApprovalStatus CodeApprovalStatus { get; set; } = WorkflowApprovalStatus.Pending;

    public int? SourceDocumentVersionId { get; set; }

    public string? TicketTitle { get; set; }
    public string? TicketDescription { get; set; }
    public string? TicketSummary { get; set; }
    public string? TicketAcceptanceCriteria { get; set; }
    public string? TicketProblem { get; set; }
    public string? TicketTechnicalNotes { get; set; }

    public string? KnowledgeContextMarkdown { get; set; }
    public string? ImplementationPlanMarkdown { get; set; }
    public string? GeneratedPatch { get; set; }
    public CodeChangeProposal? CodeProposal { get; set; }
    public PatchValidationResult? PatchValidation { get; set; }
    public string? BuildOutput { get; set; }
    public string? TestOutput { get; set; }
    public string? FailureDiagnosisMarkdown { get; set; }
    public string? CompletionSummaryMarkdown { get; set; }

    public List<Guid> KnowledgeArtefactIds { get; set; } = [];
    public List<string> AffectedFiles { get; set; } = [];
    public List<string> TraceMessages { get; set; } = [];
    public List<SemanticWorkflowMemoryItem> KnowledgeMemoryItems { get; set; } = [];
    public List<WorkflowToolCall> ToolCalls { get; set; } = [];
}

public sealed class WorkflowToolCall
{
    public string ToolName { get; init; } = string.Empty;
    public string NodeName { get; init; } = string.Empty;
    public string Status { get; init; } = "Completed";
    public string? Summary { get; init; }
    public DateTime StartedUtc { get; init; }
    public DateTime CompletedUtc { get; init; }
}
