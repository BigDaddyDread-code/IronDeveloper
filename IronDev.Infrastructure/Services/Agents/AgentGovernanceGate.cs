using IronDev.Core.Agents;
using IronDev.Core.Interfaces;

namespace IronDev.Infrastructure.Services.Agents;

public sealed class AgentGovernanceGate : IAgentGovernanceGate
{
    public AgentGovernanceDecision Evaluate(AgentDefinition definition, AgentRequest request)
    {
        if (!definition.Enabled)
            return Block($"Agent '{definition.Name}' is disabled.", AgentApprovalDecision.Invalid, AgentActionImpact.ReadOnly);

        var requestedToolNames = request.RequestedTools
            .Concat(request.RequestedToolCalls.Select(call => call.ToolName))
            .Where(tool => !string.IsNullOrWhiteSpace(tool))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var disallowedTools = requestedToolNames
            .Where(tool => !definition.AllowedTools.Contains(tool, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tool => tool, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (disallowedTools.Length > 0)
        {
            return Block(
                $"Agent '{definition.Name}' requested tools outside its declared boundary: {string.Join(", ", disallowedTools)}.",
                AgentApprovalDecision.Invalid,
                MaxImpact(request.RequestedToolCalls));
        }

        foreach (var call in request.RequestedToolCalls)
        {
            if (string.IsNullOrWhiteSpace(call.ToolName))
                return Block("Agent requested an unnamed typed tool call.", AgentApprovalDecision.Invalid, call.Impact);

            var highImpact = IsHighImpact(call);
            if (!highImpact)
                continue;

            if (request.DryRunOnly)
            {
                return Block(
                    $"High-impact tool '{call.ToolName}' cannot execute while DryRunOnly is true.",
                    AgentApprovalDecision.Missing,
                    call.Impact,
                    requiresApproval: true);
            }

            var approval = request.ApprovalEvidence;
            if (approval is null)
            {
                return Block(
                    $"High-impact tool '{call.ToolName}' requires typed approval evidence.",
                    AgentApprovalDecision.Missing,
                    call.Impact,
                    requiresApproval: true);
            }

            if (approval.Decision != AgentApprovalDecision.Approved)
            {
                return Block(
                    $"Approval for high-impact tool '{call.ToolName}' is not approved.",
                    approval.Decision,
                    call.Impact,
                    requiresApproval: true);
            }

            if (approval.ExpiresAtUtc is { } expiresAtUtc && expiresAtUtc <= DateTimeOffset.UtcNow)
            {
                return Block(
                    $"Approval for high-impact tool '{call.ToolName}' has expired.",
                    AgentApprovalDecision.Expired,
                    call.Impact,
                    requiresApproval: true);
            }

            if (string.IsNullOrWhiteSpace(request.ProposalId) ||
                string.IsNullOrWhiteSpace(request.ProposalHash) ||
                !string.Equals(approval.ProposalId, request.ProposalId, StringComparison.Ordinal) ||
                !string.Equals(approval.ProposalHash, request.ProposalHash, StringComparison.Ordinal))
            {
                return Block(
                    $"Approval for high-impact tool '{call.ToolName}' does not match the requested proposal.",
                    AgentApprovalDecision.Invalid,
                    call.Impact,
                    requiresApproval: true);
            }

            if (!string.IsNullOrWhiteSpace(call.ApprovalScope) &&
                !string.Equals(approval.Scope, call.ApprovalScope, StringComparison.OrdinalIgnoreCase))
            {
                return Block(
                    $"Approval scope '{approval.Scope}' does not match required scope '{call.ApprovalScope}' for tool '{call.ToolName}'.",
                    AgentApprovalDecision.Invalid,
                    call.Impact,
                    requiresApproval: true);
            }

            if (call.EvidenceRequired &&
                (approval.Evidence.Count == 0 || approval.Evidence.Any(evidence => string.IsNullOrWhiteSpace(evidence.Sha256))))
            {
                return Block(
                    $"Approval for high-impact tool '{call.ToolName}' is missing hashed evidence.",
                    AgentApprovalDecision.Invalid,
                    call.Impact,
                    requiresApproval: true);
            }
        }

        var requiresApproval = request.RequestedToolCalls.Any(IsHighImpact);
        return new AgentGovernanceDecision
        {
            IsAllowed = true,
            Reason = requiresApproval
                ? "Typed approval evidence satisfied agent governance."
                : "Read-only or legacy agent request satisfied agent governance.",
            ApprovalDecision = requiresApproval ? AgentApprovalDecision.Approved : AgentApprovalDecision.NotRequired,
            MaxImpact = MaxImpact(request.RequestedToolCalls),
            RequiresApproval = requiresApproval,
            Violations = []
        };
    }

    private static bool IsHighImpact(AgentToolCallRequest call) =>
        call.RequiresApproval ||
        call.AllowsFileWrites ||
        call.AllowsProcessExecution ||
        call.AllowsWorkspaceMutation ||
        call.Impact is AgentActionImpact.ProcessExecution
            or AgentActionImpact.WorkspaceMutation
            or AgentActionImpact.MemoryMutation
            or AgentActionImpact.ExternalNetwork
            or AgentActionImpact.Unknown;

    private static AgentActionImpact MaxImpact(IReadOnlyList<AgentToolCallRequest> calls)
    {
        if (calls.Count == 0)
            return AgentActionImpact.ReadOnly;

        if (calls.Any(call => call.Impact == AgentActionImpact.Unknown))
            return AgentActionImpact.Unknown;
        if (calls.Any(call => call.Impact == AgentActionImpact.WorkspaceMutation))
            return AgentActionImpact.WorkspaceMutation;
        if (calls.Any(call => call.Impact == AgentActionImpact.ProcessExecution))
            return AgentActionImpact.ProcessExecution;
        if (calls.Any(call => call.Impact == AgentActionImpact.MemoryMutation))
            return AgentActionImpact.MemoryMutation;
        if (calls.Any(call => call.Impact == AgentActionImpact.ExternalNetwork))
            return AgentActionImpact.ExternalNetwork;
        if (calls.Any(call => call.Impact == AgentActionImpact.Diagnostic))
            return AgentActionImpact.Diagnostic;

        return AgentActionImpact.ReadOnly;
    }

    private static AgentGovernanceDecision Block(
        string reason,
        AgentApprovalDecision approvalDecision,
        AgentActionImpact impact,
        bool requiresApproval = false) =>
        new()
        {
            IsAllowed = false,
            Reason = reason,
            ApprovalDecision = approvalDecision,
            MaxImpact = impact,
            RequiresApproval = requiresApproval,
            Violations = [reason]
        };
}
