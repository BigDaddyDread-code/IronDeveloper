using System.Text.Json;
using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
public sealed class PatchProposalEvidencePackageTests
{
    private readonly IPatchProposalEvidencePackageWorkflow _workflow = new PatchProposalEvidencePackageWorkflow();

    [TestMethod]
    public void NullRequest_ReturnsInvalidRequest()
    {
        var result = _workflow.Prepare(null);

        Assert.AreEqual(PatchProposalEvidencePackageStatus.InvalidRequest, result.Status);
        AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow("workflow", PatchProposalEvidencePackageReason.MissingWorkflowRunId)]
    [DataRow("step", PatchProposalEvidencePackageReason.MissingWorkflowStepId)]
    [DataRow("package", PatchProposalEvidencePackageReason.MissingPackageReference)]
    [DataRow("project", PatchProposalEvidencePackageReason.MissingProjectReference)]
    [DataRow("target", PatchProposalEvidencePackageReason.MissingTargetReference)]
    public void MissingIdentity_ReturnsInvalidRequest(string field, PatchProposalEvidencePackageReason expectedReason)
    {
        var request = field switch
        {
            "workflow" => ValidRequest() with { WorkflowRunId = " " },
            "step" => ValidRequest() with { WorkflowStepId = " " },
            "package" => ValidRequest() with { PatchProposalEvidencePackageReferenceId = " " },
            "project" => ValidRequest() with { ProjectReferenceId = " " },
            _ => ValidRequest() with { TargetReferenceId = " " }
        };

        var result = _workflow.Prepare(request);

        Assert.AreEqual(PatchProposalEvidencePackageStatus.InvalidRequest, result.Status);
        CollectionAssert.Contains(result.Reasons.ToList(), expectedReason);
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void UnknownTargetKind_ReturnsInvalidRequest()
    {
        var result = _workflow.Prepare(ValidRequest() with { TargetKind = PatchProposalTargetKind.Unknown });

        Assert.AreEqual(PatchProposalEvidencePackageStatus.InvalidRequest, result.Status);
        CollectionAssert.Contains(result.Reasons.ToList(), PatchProposalEvidencePackageReason.InvalidTargetKind);
        AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow("affected", "raw prompt leaked")]
    [DataRow("validation", "raw completion leaked")]
    [DataRow("evidence", "raw tool output leaked")]
    [DataRow("gate", "private reasoning leaked")]
    [DataRow("risk", "hidden reasoning leaked")]
    public void UnsafeReferenceSummary_FailsClosedWithoutEcho(string field, string marker)
    {
        var request = field switch
        {
            "affected" => ValidRequest() with { AffectedAreas = [Affected(summary: marker)] },
            "validation" => ValidRequest() with { ExpectedValidationReferences = [ExpectedValidation(summary: marker)] },
            "evidence" => ValidRequest() with { EvidenceReferences = [Evidence(summary: marker)] },
            "gate" => ValidRequest() with { GateHints = [Gate(summary: marker)] },
            _ => ValidRequest() with { Risks = [Risk(summary: marker)] }
        };

        var result = _workflow.Prepare(request);
        var json = JsonSerializer.Serialize(result);

        Assert.AreEqual(PatchProposalEvidencePackageStatus.InvalidRequest, result.Status);
        CollectionAssert.Contains(result.Reasons.ToList(), PatchProposalEvidencePackageReason.UnsafeInput);
        Assert.IsFalse(json.Contains(marker, StringComparison.OrdinalIgnoreCase), $"Unsafe marker was echoed: {marker}");
        AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow("patch payload")]
    [DataRow("diff payload")]
    [DataRow("source content")]
    [DataRow("patch applied")]
    [DataRow("source applied")]
    [DataRow("approved")]
    [DataRow("approval satisfied")]
    [DataRow("workflow continued")]
    public void UnsafeAuthorityMarkers_FailClosed(string marker)
    {
        var result = _workflow.Prepare(ValidRequest() with { SafeChangeIntentSummary = marker });
        var json = JsonSerializer.Serialize(result);

        Assert.AreEqual(PatchProposalEvidencePackageStatus.InvalidRequest, result.Status);
        CollectionAssert.Contains(result.Reasons.ToList(), PatchProposalEvidencePackageReason.UnsafeInput);
        Assert.IsFalse(json.Contains(marker, StringComparison.OrdinalIgnoreCase), $"Unsafe marker was echoed: {marker}");
        AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow("runner")]
    [DataRow("dryrun")]
    [DataRow("route")]
    public void BlockingWorkflowGateMaterial_BlocksPackageProduction(string blocker)
    {
        var request = blocker switch
        {
            "runner" => ValidRequest() with { StepEvaluation = HumanApprovalPackageFixtures.StepEvaluation(WorkflowStepRunnerEligibility.BlockedByBoundary) },
            "dryrun" => ValidRequest() with { DryRunResult = HumanApprovalPackageFixtures.DryRun(WorkflowDryRunStatus.BlockedByPolicyPreflight) },
            _ => ValidRequest() with { RouteSuggestion = HumanApprovalPackageFixtures.Route(BoxedLangGraphRouteLabel.BlockedPolicyPreflight) }
        };

        var result = _workflow.Prepare(request);

        Assert.AreEqual(PatchProposalEvidencePackageStatus.BlockedByWorkflowGate, result.Status);
        AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow("source")]
    [DataRow("approval")]
    [DataRow("transition")]
    public void RouteSuggestionAuthority_BlocksPackageProduction(string authority)
    {
        var route = authority switch
        {
            "source" => HumanApprovalPackageFixtures.Route(BoxedLangGraphRouteLabel.DryRunReviewMaterialAvailable) with { SourceChangeAllowed = true },
            "approval" => HumanApprovalPackageFixtures.Route(BoxedLangGraphRouteLabel.DryRunReviewMaterialAvailable) with { ApprovalChangeAllowed = true },
            _ => HumanApprovalPackageFixtures.Route(BoxedLangGraphRouteLabel.DryRunReviewMaterialAvailable) with { WorkflowStateChangeAllowed = true }
        };

        var result = _workflow.Prepare(ValidRequest() with { RouteSuggestion = route });

        Assert.AreEqual(PatchProposalEvidencePackageStatus.BlockedByWorkflowGate, result.Status);
        CollectionAssert.Contains(result.Reasons.ToList(), PatchProposalEvidencePackageReason.BlockedByRouteSuggestion);
        AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow("implementation")]
    [DataRow("sourceRequirement")]
    [DataRow("humanApproval")]
    public void InvalidUpstreamPackage_BlocksPackageProduction(string package)
    {
        var request = package switch
        {
            "implementation" => ValidRequest() with { ImplementationProposal = SourceApplyApprovalRequirementContractTests.ImplementationProposal(ImplementationProposalPackageCandidateStatus.BlockedByWorkflowGate) },
            "sourceRequirement" => ValidRequest() with { SourceApplyApprovalRequirement = SourceRequirement(SourceApplyApprovalRequirementStatus.BlockedByWorkflowGate) },
            _ => ValidRequest() with { HumanApprovalPackage = SourceApplyApprovalRequirementContractTests.HumanApprovalPackage(SourceApplyApprovalRequirementContractTests.ImplementationProposal(), HumanApprovalPackageCandidateStatus.BlockedByWorkflowGate) }
        };

        var result = _workflow.Prepare(request);

        Assert.AreEqual(PatchProposalEvidencePackageStatus.BlockedByWorkflowGate, result.Status);
        AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow("summary")]
    [DataRow("implementation")]
    [DataRow("sourceRequirement")]
    [DataRow("affected")]
    [DataRow("validation")]
    [DataRow("evidence")]
    [DataRow("gate")]
    public void MissingRequiredPatchProposalEvidence_ReturnsMissingRequiredEvidence(string missing)
    {
        var request = missing switch
        {
            "summary" => ValidRequest() with { SafeChangeIntentSummary = " " },
            "implementation" => ValidRequest() with { ImplementationProposal = null },
            "sourceRequirement" => ValidRequest() with { SourceApplyApprovalRequirement = null },
            "affected" => ValidRequest() with { AffectedAreas = [] },
            "validation" => ValidRequest() with { ExpectedValidationReferences = [] },
            "evidence" => ValidRequest() with { EvidenceReferences = [] },
            _ => ValidRequest() with { GateHints = [] }
        };

        var result = _workflow.Prepare(request);

        Assert.AreEqual(PatchProposalEvidencePackageStatus.MissingRequiredPatchProposalEvidence, result.Status);
        Assert.IsTrue(result.MissingEvidence.Count > 0);
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void ValidMaterial_ProducesPatchProposalEvidencePackage()
    {
        var result = _workflow.Prepare(ValidRequest());

        Assert.AreEqual(PatchProposalEvidencePackageStatus.PatchProposalEvidencePackageProduced, result.Status);
        Assert.IsTrue(result.PackageReferenceId.StartsWith("patch-proposal-evidence-package:", StringComparison.Ordinal));
        CollectionAssert.Contains(result.Reasons.ToList(), PatchProposalEvidencePackageReason.PackageOnly);
        CollectionAssert.Contains(result.Reasons.ToList(), PatchProposalEvidencePackageReason.PatchNotGenerated);
        CollectionAssert.Contains(result.Reasons.ToList(), PatchProposalEvidencePackageReason.DiffNotGenerated);
        CollectionAssert.Contains(result.Reasons.ToList(), PatchProposalEvidencePackageReason.SourceNotApplied);
        CollectionAssert.Contains(result.Reasons.ToList(), PatchProposalEvidencePackageReason.PatchNotApplied);
        CollectionAssert.Contains(result.Reasons.ToList(), PatchProposalEvidencePackageReason.FilesNotMutated);
        CollectionAssert.Contains(result.Reasons.ToList(), PatchProposalEvidencePackageReason.SourceFilesNotRead);
        CollectionAssert.Contains(result.Reasons.ToList(), PatchProposalEvidencePackageReason.ApprovalNotSatisfied);
        CollectionAssert.Contains(result.Reasons.ToList(), PatchProposalEvidencePackageReason.PolicyNotSatisfied);
        CollectionAssert.Contains(result.Reasons.ToList(), PatchProposalEvidencePackageReason.WorkflowNotTransitioned);
        CollectionAssert.Contains(result.SafePackageSummaryLines.ToList(), "Implementation proposal package is review material only.");
        CollectionAssert.Contains(result.SafePackageSummaryLines.ToList(), "Source apply approval requirement is requirement material only.");
        CollectionAssert.Contains(result.SafePackageSummaryLines.ToList(), "Human approval package is review material only.");
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void ProducedPackage_DoesNotMeanPatchReady()
    {
        var result = _workflow.Prepare(ValidRequest());

        Assert.AreEqual(PatchProposalEvidencePackageStatus.PatchProposalEvidencePackageProduced, result.Status);
        Assert.IsFalse(result.IsPatch);
        Assert.IsFalse(result.IsDiff);
        Assert.IsFalse(result.CanGeneratePatch);
        Assert.IsFalse(result.CanApplyPatch);
    }

    [TestMethod]
    public void HumanApprovalPackage_IsReviewMaterialOnly()
    {
        var request = ValidRequest();
        var result = _workflow.Prepare(request);

        Assert.IsNotNull(request.HumanApprovalPackage);
        Assert.IsFalse(result.CanSatisfyApproval);
        CollectionAssert.Contains(result.Reasons.ToList(), PatchProposalEvidencePackageReason.ApprovalNotSatisfied);
    }

    [TestMethod]
    public void AffectedFileReference_IsNotSourceAccess()
    {
        var result = _workflow.Prepare(ValidRequest());

        Assert.AreEqual(PatchProposalAffectedAreaKind.FilePathReference, result.AffectedAreas.Single().Kind);
        Assert.IsFalse(result.CanReadSourceFiles);
        Assert.IsFalse(result.CanMutateFiles);
    }

    [TestMethod]
    public void ExpectedValidationReference_IsNotValidationProof()
    {
        var result = _workflow.Prepare(ValidRequest());

        Assert.AreEqual(PatchProposalExpectedValidationKind.FocusedTestBandReference, result.ExpectedValidationReferences.Single().Kind);
        Assert.IsFalse(result.CanRunCommand);
        Assert.IsFalse(result.CanInvokeTool);
    }

    [TestMethod]
    public void SameRequest_GivesDeterministicResult()
    {
        var request = ValidRequest();
        var first = JsonSerializer.Serialize(_workflow.Prepare(request));
        var second = JsonSerializer.Serialize(_workflow.Prepare(request));

        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public void ResultSerializesWithoutRawPrivateOrPatchPayloadMaterial()
    {
        var json = JsonSerializer.Serialize(_workflow.Prepare(ValidRequest()));

        AssertDoesNotContainAny(
            json,
            "raw prompt",
            "raw completion",
            "raw tool output",
            "private reasoning",
            "hidden reasoning",
            "whole patch",
            "entire patch",
            "patch payload",
            "diff payload",
            "source content");
    }

    internal static PatchProposalEvidencePackageRequest ValidRequest()
    {
        var sourceRequest = SourceApplyApprovalRequirementContractTests.ValidRequest();
        var sourceRequirement = new SourceApplyApprovalRequirementContract().Evaluate(sourceRequest);

        return new()
        {
            WorkflowRunId = "workflow-run-138",
            WorkflowStepId = "workflow-step-patch-proposal-evidence-package",
            PatchProposalEvidencePackageReferenceId = "patch-proposal-evidence-package-138",
            ProjectReferenceId = "project-138",
            TargetReferenceId = "source-apply-target-138",
            TargetKind = PatchProposalTargetKind.SourceApplyCandidate,
            SafeChangeIntentSummary = "Collect supplied review evidence for a later governed patch proposal.",
            ImplementationProposal = sourceRequest.ImplementationProposal,
            SourceApplyApprovalRequirement = sourceRequirement,
            HumanApprovalPackage = sourceRequest.HumanApprovalPackage,
            AffectedAreas =
            [
                Affected(PatchProposalAffectedAreaKind.FilePathReference, "affected-file-reference-138", "Affected file reference only.")
            ],
            ExpectedValidationReferences =
            [
                ExpectedValidation(PatchProposalExpectedValidationKind.FocusedTestBandReference, "focused-test-band-reference-138", "Focused validation is expected later.")
            ],
            EvidenceReferences =
            [
                Evidence(PatchProposalEvidenceKind.ImplementationProposalPackageReference, sourceRequest.ImplementationProposal!.ProposalPackageReferenceId, "Implementation proposal package reference."),
                Evidence(PatchProposalEvidenceKind.SourceApplyApprovalRequirementReference, sourceRequirement.RequirementReferenceId, "Source apply approval requirement reference."),
                Evidence(PatchProposalEvidenceKind.HumanApprovalPackageReference, sourceRequest.HumanApprovalPackage!.PackageReferenceId, "Human approval package reference.")
            ],
            GateHints =
            [
                Gate(PatchProposalGateKind.HumanApprovalRequired, PatchProposalSeverityHint.Critical, "Human approval is required later."),
                Gate(PatchProposalGateKind.SourceApplyApprovalRequirementRequired, PatchProposalSeverityHint.Critical, "Source apply approval requirement remains required."),
                Gate(PatchProposalGateKind.PatchMaterialForbidden, PatchProposalSeverityHint.Critical, "Patch material is forbidden in this package.")
            ],
            Risks =
            [
                Risk(PatchProposalRiskKind.MissingApprovalRequirement, PatchProposalSeverityHint.Critical, "Approval requirement remains unsatisfied."),
                Risk(PatchProposalRiskKind.CandidatePackageConfusedWithPatch, PatchProposalSeverityHint.High, "Evidence package is not a patch.")
            ],
            StepEvaluation = HumanApprovalPackageFixtures.StepEvaluation(WorkflowStepRunnerEligibility.EligibleForFutureExecution),
            DryRunResult = HumanApprovalPackageFixtures.DryRun(WorkflowDryRunStatus.DryRunCompleted),
            RouteSuggestion = HumanApprovalPackageFixtures.Route(BoxedLangGraphRouteLabel.DryRunReviewMaterialAvailable),
            CorrelationId = "correlation-138"
        };
    }

    internal static SourceApplyApprovalRequirementResult SourceRequirement(SourceApplyApprovalRequirementStatus status) =>
        new SourceApplyApprovalRequirementContract().Evaluate(SourceApplyApprovalRequirementContractTests.ValidRequest()) with { Status = status };

    internal static PatchProposalAffectedAreaReference Affected(
        PatchProposalAffectedAreaKind kind = PatchProposalAffectedAreaKind.FilePathReference,
        string referenceId = "affected-area-138",
        string? summary = "Affected area reference only.") => new()
    {
        Kind = kind,
        ReferenceId = referenceId,
        SafeSummary = summary
    };

    internal static PatchProposalExpectedValidationReference ExpectedValidation(
        PatchProposalExpectedValidationKind kind = PatchProposalExpectedValidationKind.FocusedTestBandReference,
        string referenceId = "expected-validation-138",
        string? summary = "Expected validation reference only.") => new()
    {
        Kind = kind,
        ReferenceId = referenceId,
        SafeSummary = summary
    };

    internal static PatchProposalEvidenceReference Evidence(
        PatchProposalEvidenceKind kind = PatchProposalEvidenceKind.ExternalArtifactReference,
        string referenceId = "evidence-138",
        string? summary = "Evidence reference only.") => new()
    {
        Kind = kind,
        ReferenceId = referenceId,
        SafeSummary = summary
    };

    internal static PatchProposalGateHint Gate(
        PatchProposalGateKind kind = PatchProposalGateKind.ManualReviewRequired,
        PatchProposalSeverityHint severity = PatchProposalSeverityHint.High,
        string? summary = "Manual review remains required.") => new()
    {
        Kind = kind,
        SeverityHint = severity,
        SafeSummary = summary
    };

    internal static PatchProposalRisk Risk(
        PatchProposalRiskKind kind = PatchProposalRiskKind.OverclaimRisk,
        PatchProposalSeverityHint severity = PatchProposalSeverityHint.High,
        string? summary = "Evidence package must not overclaim.") => new()
    {
        Kind = kind,
        SeverityHint = severity,
        SafeSummary = summary
    };

    internal static void AssertNoAuthority(PatchProposalEvidencePackageResult result)
    {
        Assert.IsTrue(result.IsPackageOnly);
        Assert.IsFalse(result.IsPatch);
        Assert.IsFalse(result.IsDiff);
        Assert.IsFalse(result.IsSourceApply);
        Assert.IsFalse(result.IsImplementation);
        Assert.IsFalse(result.CanGeneratePatch);
        Assert.IsFalse(result.CanApplyPatch);
        Assert.IsFalse(result.CanApplySource);
        Assert.IsFalse(result.CanMutateFiles);
        Assert.IsFalse(result.CanReadSourceFiles);
        Assert.IsFalse(result.CanRunCommand);
        Assert.IsFalse(result.CanInvokeTool);
        Assert.IsFalse(result.CanDispatchAgent);
        Assert.IsFalse(result.CanCallModel);
        Assert.IsFalse(result.CanBuildPrompt);
        Assert.IsFalse(result.CanSatisfyApproval);
        Assert.IsFalse(result.CanSatisfyPolicy);
        Assert.IsFalse(result.CanTransitionWorkflow);
        Assert.IsFalse(result.CanCreateTicket);
        Assert.IsFalse(result.CanPromoteMemory);
        Assert.IsFalse(result.CanActivateRetrieval);
        Assert.IsFalse(result.CanWriteSql);
    }

    private static void AssertDoesNotContainAny(string text, params string[] markers)
    {
        foreach (var marker in markers)
            Assert.IsFalse(text.Contains(marker, StringComparison.OrdinalIgnoreCase), $"Unexpected marker '{marker}'.");
    }
}
