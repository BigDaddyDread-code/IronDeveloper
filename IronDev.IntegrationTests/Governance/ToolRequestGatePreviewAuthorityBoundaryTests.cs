using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
public sealed class ToolRequestGatePreviewAuthorityBoundaryTests
{
    [TestMethod]
    public void PreviewResult_IsEvidenceForNothingAuthoritative()
    {
        var result = ProducedResult();

        Assert.IsTrue(result.IsPreviewOnly);
        Assert.IsFalse(result.IsToolExecution);
        Assert.IsFalse(result.CanInvokeTool);
        Assert.IsFalse(result.CanAuthorizeTool);
        Assert.IsFalse(result.CanReserveTool);
        Assert.IsFalse(result.CanRunCommand);
        Assert.IsFalse(result.CanCallModel);
        Assert.IsFalse(result.CanBuildPrompt);
        Assert.IsFalse(result.CanDispatchAgent);
        Assert.IsFalse(result.CanSatisfyApproval);
        Assert.IsFalse(result.CanSatisfyPolicy);
        Assert.IsFalse(result.CanTransitionWorkflow);
        Assert.IsFalse(result.CanMutateSource);
        Assert.IsFalse(result.CanApplyPatch);
        Assert.IsFalse(result.CanCreateTicket);
        Assert.IsFalse(result.CanPromoteMemory);
        Assert.IsFalse(result.CanActivateRetrieval);
    }

    [DataTestMethod]
    [DataRow("approval evidence")]
    [DataRow("policy evidence")]
    [DataRow("workflow transition evidence")]
    [DataRow("dry-run evidence")]
    [DataRow("A2A validation evidence")]
    [DataRow("source mutation evidence")]
    [DataRow("patch apply evidence")]
    [DataRow("memory promotion evidence")]
    [DataRow("retrieval approval evidence")]
    [DataRow("tool execution evidence")]
    [DataRow("tool authorization evidence")]
    public void PreviewResult_IsNotEquivalentToGateEvidence(string evidenceKind)
    {
        var result = ProducedResult();

        Assert.AreEqual(ToolRequestGatePreviewCandidateStatus.GatePreviewProduced, result.Status, evidenceKind);
        Assert.IsTrue(result.Reasons.Contains(ToolRequestGatePreviewCandidateReason.PreviewOnly), evidenceKind);
        Assert.IsTrue(result.Reasons.Contains(ToolRequestGatePreviewCandidateReason.SuppliedEvidenceOnly), evidenceKind);
        Assert.IsFalse(result.Reasons.Any(reason => reason.ToString().Contains("Satisfied", StringComparison.Ordinal) &&
                                                    reason is not ToolRequestGatePreviewCandidateReason.ApprovalNotSatisfied and
                                                        not ToolRequestGatePreviewCandidateReason.PolicyNotSatisfied), evidenceKind);
        Assert.IsFalse(result.IsToolExecution, evidenceKind);
        Assert.IsFalse(result.CanSatisfyApproval, evidenceKind);
        Assert.IsFalse(result.CanSatisfyPolicy, evidenceKind);
        Assert.IsFalse(result.CanTransitionWorkflow, evidenceKind);
    }

    [DataTestMethod]
    [DataRow("approval halt")]
    [DataRow("policy preflight")]
    [DataRow("A2A validation")]
    [DataRow("runner evidence")]
    [DataRow("approval-required halt")]
    [DataRow("policy block")]
    [DataRow("A2A block")]
    [DataRow("missing evidence")]
    [DataRow("route authority")]
    [DataRow("ticket creation authority")]
    [DataRow("source apply authority")]
    [DataRow("command execution")]
    [DataRow("tool output")]
    [DataRow("capability authorization")]
    public void PreviewResult_CannotBypassOrSatisfyLaterBoundaries(string boundary)
    {
        var result = ProducedResult();

        Assert.IsTrue(result.IsPreviewOnly, boundary);
        Assert.IsTrue(result.MissingGateMaterial.Count > 0 || result.GateRequirementHints.Count > 0, boundary);
        Assert.IsFalse(result.CanSatisfyApproval, boundary);
        Assert.IsFalse(result.CanSatisfyPolicy, boundary);
        Assert.IsFalse(result.CanTransitionWorkflow, boundary);
        Assert.IsFalse(result.CanAuthorizeTool, boundary);
        Assert.IsFalse(result.CanInvokeTool, boundary);
        Assert.IsFalse(result.CanRunCommand, boundary);
        Assert.IsFalse(result.CanCreateTicket, boundary);
        Assert.IsFalse(result.CanMutateSource, boundary);
        Assert.IsFalse(result.CanApplyPatch, boundary);
    }

    [TestMethod]
    public void PreviewResult_DoesNotConvertGateHintsIntoSatisfiedGates()
    {
        var result = ProducedResult();

        Assert.IsTrue(result.GateRequirementHints.Any(hint => hint.Kind == ToolRequestGateKind.ApprovalRequired));
        Assert.IsTrue(result.GateRequirementHints.Any(hint => hint.Kind == ToolRequestGateKind.PolicyEvidenceRequired));
        Assert.IsTrue(result.GateRequirementHints.Any(hint => hint.Kind == ToolRequestGateKind.HumanReviewRequired));
        Assert.IsFalse(result.CanSatisfyApproval);
        Assert.IsFalse(result.CanSatisfyPolicy);
    }

    private static ToolRequestGatePreviewCandidateResult ProducedResult() =>
        new ToolRequestGatePreviewCandidateWorkflow().Preview(new ToolRequestGatePreviewCandidateRequest
        {
            WorkflowRunId = "workflow-run-1",
            WorkflowStepId = "workflow-step-1",
            ToolRequestPreviewReferenceId = "tool-preview-1",
            CapabilityName = "quality.gate.preview",
            SafePurposeSummary = "Preview gate requirements for human review.",
            InputReferences =
            [
                new ToolRequestPreviewInputReference
                {
                    Kind = ToolRequestPreviewInputKind.WorkflowStepEvaluationReference,
                    ReferenceId = "runner-evaluation-1",
                    SafeSummary = "Runner evaluation reference."
                }
            ],
            ExpectedOutputReferences =
            [
                new ToolRequestPreviewOutputReference
                {
                    Kind = ToolRequestPreviewOutputKind.ValidationReportReference,
                    ReferenceId = "validation-report-1",
                    SafeSummary = "Expected validation report reference."
                }
            ],
            GateRequirementHints =
            [
                new ToolRequestGateRequirementHint
                {
                    Kind = ToolRequestGateKind.ApprovalRequired,
                    SeverityHint = ToolRequestGateSeverityHint.High,
                    SafeSummary = "Approval remains required later."
                },
                new ToolRequestGateRequirementHint
                {
                    Kind = ToolRequestGateKind.PolicyEvidenceRequired,
                    SeverityHint = ToolRequestGateSeverityHint.High,
                    SafeSummary = "Policy evidence remains required later."
                },
                new ToolRequestGateRequirementHint
                {
                    Kind = ToolRequestGateKind.HumanReviewRequired,
                    SeverityHint = ToolRequestGateSeverityHint.High,
                    SafeSummary = "Human review remains required later."
                }
            ],
            Risks =
            [
                new ToolRequestPreviewRisk
                {
                    Kind = ToolRequestPreviewRiskKind.ToolExecutionRisk,
                    SeverityHint = ToolRequestGateSeverityHint.High,
                    SafeSummary = "Tool execution remains separate."
                }
            ]
        });
}
