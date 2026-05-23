using IronDev.Core.Agents;

namespace IronDev.Infrastructure.Services.Agents;

public sealed class EvidenceValidationService
{
    public EvidenceValidationResult Validate(IReadOnlyList<AgentToolResult> toolResults, IReadOnlyList<string> requiredEvidence)
    {
        var present = new List<string>();
        var findings = new List<EvidenceValidationFinding>();

        foreach (var toolResult in toolResults)
        {
            if (!string.Equals(toolResult.Status, "Succeeded", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new EvidenceValidationFinding
                {
                    Severity = "Error",
                    Message = $"{toolResult.ToolName} did not succeed: {toolResult.Summary}",
                    EvidenceRef = toolResult.RequestId
                });
                continue;
            }

            present.Add(toolResult.ToolName);
            if (toolResult.EvidenceRefs.Count == 0 && RequiresConcreteEvidence(toolResult.ToolName))
            {
                findings.Add(new EvidenceValidationFinding
                {
                    Severity = "Warning",
                    Message = $"{toolResult.ToolName} succeeded without concrete evidence refs.",
                    EvidenceRef = toolResult.RequestId
                });
            }
        }

        var missing = requiredEvidence
            .Where(required => !present.Contains(required, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        foreach (var missingItem in missing)
        {
            findings.Add(new EvidenceValidationFinding
            {
                Severity = "Error",
                Message = $"Missing required evidence from {missingItem}.",
                EvidenceRef = missingItem
            });
        }

        var hasErrors = findings.Any(finding => string.Equals(finding.Severity, "Error", StringComparison.OrdinalIgnoreCase));
        return new EvidenceValidationResult
        {
            Status = hasErrors ? "Failed" : "Passed",
            RequiredEvidence = requiredEvidence,
            PresentEvidence = present,
            MissingEvidence = missing,
            Findings = findings.Count == 0
                ? [new EvidenceValidationFinding { Severity = "Info", Message = "Required evidence is present.", EvidenceRef = "evidence-validation" }]
                : findings
        };
    }

    private static bool RequiresConcreteEvidence(string toolName) =>
        toolName is "memory.search" or "code.search" or "trace.read" or "failure.latest";
}
