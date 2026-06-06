using IronDev.Core.Agents;
using IronDev.Core.Interfaces;
using System.Text.RegularExpressions;

namespace IronDev.Infrastructure.Services.Agents;

public sealed class AgentGovernanceGate : IAgentGovernanceGate
{
    private static readonly Regex Sha256Regex = new(@"^[A-Fa-f0-9]{64}$", RegexOptions.Compiled);

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

        var requestedLegacyHighImpactTools = request.RequestedTools
            .Where(tool => AgentToolCapabilityCatalog.IsHighImpact(tool))
            .Where(tool => !request.RequestedToolCalls.Any(call =>
                string.Equals(call.ToolName, tool, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tool => tool, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (requestedLegacyHighImpactTools.Length > 0)
        {
            return Block(
                $"Legacy requested high-impact tools require typed tool-call metadata: {string.Join(", ", requestedLegacyHighImpactTools)}.",
                AgentApprovalDecision.Missing,
                AgentActionImpact.WorkspaceMutation,
                requiresApproval: true);
        }

        var approval = request.ApprovalEvidence;

        foreach (var call in request.RequestedToolCalls)
        {
            if (string.IsNullOrWhiteSpace(call.ToolName))
                return Block("Agent requested an unnamed typed tool call.", AgentApprovalDecision.Invalid, call.Impact);

            var highImpact = IsHighImpact(call);
            if (!highImpact)
                continue;

            if (string.IsNullOrWhiteSpace(call.ToolCallId))
            {
                return Block(
                    $"High-impact tool '{call.ToolName}' is missing ToolCallId metadata.",
                    AgentApprovalDecision.Invalid,
                    call.Impact,
                    requiresApproval: true);
            }

            if (request.DryRunOnly)
            {
                return Block(
                    $"High-impact tool '{call.ToolName}' cannot execute while DryRunOnly is true.",
                    AgentApprovalDecision.Missing,
                    call.Impact,
                    requiresApproval: true);
            }

            if (approval is null)
            {
                return Block(
                    $"High-impact tool '{call.ToolName}' requires typed approval evidence.",
                    AgentApprovalDecision.Missing,
                    call.Impact,
                    requiresApproval: true);
            }

            if (string.IsNullOrWhiteSpace(approval.ApprovalId))
            {
                return Block(
                    $"High-impact tool '{call.ToolName}' approval evidence is missing ApprovalId.",
                    AgentApprovalDecision.Invalid,
                    call.Impact,
                    requiresApproval: true);
            }

            if (approval.ApprovedToolCallIds.All(approvedId => !string.Equals(approvedId, call.ToolCallId, StringComparison.Ordinal)))
            {
                return Block(
                    $"Approval evidence for high-impact tool '{call.ToolName}' does not include ToolCallId '{call.ToolCallId}'.",
                    AgentApprovalDecision.Invalid,
                    call.Impact,
                    requiresApproval: true);
            }

            if (approval.Evidence.Any(evidence => !IsEvidenceItemComplete(evidence, out _)))
            {
                return Block(
                    $"Approval for high-impact tool '{call.ToolName}' has invalid evidence metadata.",
                    AgentApprovalDecision.Invalid,
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

            if ((call.EvidenceRequired || call.EvidenceSourceIds.Count > 0) &&
                approval.Evidence.Count == 0)
            {
                return Block(
                    $"Approval for high-impact tool '{call.ToolName}' has invalid evidence metadata.",
                    AgentApprovalDecision.Invalid,
                    call.Impact,
                    requiresApproval: true);
            }

            if (call.EvidenceSourceIds.Count > 0)
            {
                var evidenceIds = new HashSet<string>(
                    approval.Evidence
                        .Select(evidence => evidence.EvidenceId),
                    StringComparer.Ordinal);

                var missingEvidenceSourceIds = call.EvidenceSourceIds
                    .Where(sourceId => !evidenceIds.Contains(sourceId))
                    .ToArray();

                if (missingEvidenceSourceIds.Length > 0)
                {
                    return Block(
                        $"Approval for high-impact tool '{call.ToolName}' references EvidenceSourceIds not in approval evidence: {string.Join(", ", missingEvidenceSourceIds)}.",
                        AgentApprovalDecision.Invalid,
                        call.Impact,
                        requiresApproval: true);
                }
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
        AgentToolCapabilityCatalog.IsHighImpact(call.ToolName) ||
        call.RequiresApproval ||
        call.AllowsFileWrites ||
        call.AllowsProcessExecution ||
        call.AllowsWorkspaceMutation ||
        call.Impact is AgentActionImpact.ProcessExecution
            or AgentActionImpact.WorkspaceMutation
            or AgentActionImpact.MemoryMutation
            or AgentActionImpact.ExternalNetwork
            or AgentActionImpact.Unknown;

    private static bool IsEvidenceItemComplete(AgentEvidenceItem item, out string? failureReason)
    {
        if (string.IsNullOrWhiteSpace(item.EvidenceId))
        {
            failureReason = "missing evidence item evidence-id";
            return false;
        }

        if (string.IsNullOrWhiteSpace(item.Kind))
        {
            failureReason = "missing evidence item kind";
            return false;
        }

        if (string.IsNullOrWhiteSpace(item.ProducedBy))
        {
            failureReason = "missing evidence producer";
            return false;
        }

        if (!Sha256Regex.IsMatch(item.Sha256))
        {
            failureReason = $"invalid evidence hash '{item.Sha256}'";
            return false;
        }

        failureReason = null;
        return true;
    }

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
