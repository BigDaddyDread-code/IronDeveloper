using System.Text.Json;
using IronDev.Core.Workflow;
using IronDev.Infrastructure.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("HumanApprovedApply")]
public sealed class HumanApprovedApplyBoundaryTests
{
    [TestMethod]
    public void HumanApprovedApply_SourceApplyRequirementIsNotApprovalOrSourceApply()
    {
        var result = new SourceApplyApprovalRequirementContract()
            .Evaluate(SourceApplyApprovalRequirementContractTests.ValidRequest());

        Assert.AreEqual(SourceApplyApprovalRequirementStatus.ApprovalRequired, result.Status);
        SourceApplyApprovalRequirementContractTests.AssertNoAuthority(result);
        AssertAuthorityFlagsFalse(
            result,
            nameof(SourceApplyApprovalRequirementResult.IsRequirementOnly));
    }

    [TestMethod]
    public void HumanApprovedApply_HumanApprovalPackageIsNotApprovalSatisfaction()
    {
        var result = SourceApplyApprovalRequirementContractTests.ValidRequest().HumanApprovalPackage!;

        Assert.AreEqual(HumanApprovalPackageCandidateStatus.ApprovalPackageProduced, result.Status);
        HumanApprovalPackageFixtures.AssertNoAuthority(result);
        AssertAuthorityFlagsFalse(
            result,
            nameof(HumanApprovalPackageCandidateResult.IsPackageOnly));
    }

    [TestMethod]
    public void HumanApprovedApply_PatchProposalEvidencePackageIsNotPatchOrApply()
    {
        var result = new PatchProposalEvidencePackageWorkflow()
            .Prepare(PatchProposalEvidencePackageTests.ValidRequest());

        Assert.AreEqual(PatchProposalEvidencePackageStatus.PatchProposalEvidencePackageProduced, result.Status);
        PatchProposalEvidencePackageTests.AssertNoAuthority(result);
        AssertAuthorityFlagsFalse(
            result,
            nameof(PatchProposalEvidencePackageResult.IsPackageOnly));
    }

    [TestMethod]
    public void HumanApprovedApply_ControlledApplyPlanIsNotExecution()
    {
        var result = new ControlledApplyPlanWorkflow()
            .Prepare(ControlledApplyPlanTests.ValidRequest());

        Assert.AreEqual(ControlledApplyPlanStatus.ControlledApplyPlanPrepared, result.Status);
        ControlledApplyPlanTests.AssertNoAuthority(result);
        AssertSpecificFalse(
            result,
            nameof(ControlledApplyPlanResult.IsExecution),
            nameof(ControlledApplyPlanResult.IsSourceApply),
            nameof(ControlledApplyPlanResult.IsPatchApplication),
            nameof(ControlledApplyPlanResult.CanApplySource),
            nameof(ControlledApplyPlanResult.CanApplyPatch),
            nameof(ControlledApplyPlanResult.CanSatisfyApproval),
            nameof(ControlledApplyPlanResult.CanSatisfyPolicy),
            nameof(ControlledApplyPlanResult.CanTransitionWorkflow));
    }

    [TestMethod]
    public void HumanApprovedApply_DryRunReceiptIsNotDryRunExecution()
    {
        var record = ValidDryRunRecord();
        var validation = new ApplyDryRunStoreValidator().ValidateCreate(record);

        Assert.IsTrue(validation.IsValid, string.Join("; ", validation.Issues.Select(issue => issue.Message)));
        AssertAuthorityFlagsFalse(
            record,
            nameof(ApplyDryRunRecord.IsStoreRecordOnly));
    }

    [TestMethod]
    public async Task HumanApprovedApply_ApplyPreviewIsReadOnlyAndCannotAuthorizeApply()
    {
        var service = new ApplyPreviewService(new FakeDryRunStore([ValidDryRunSummary()]));

        var preview = await service.GetPreviewAsync(new ApplyPreviewRequest
        {
            WorkflowRunId = "workflow-run-pr143",
            WorkflowStepId = "workflow-step-pr143",
            ControlledApplyPlanReferenceId = "controlled-apply-plan-pr143"
        });

        Assert.AreEqual(ApplyPreviewStatus.PreviewAvailable, preview.Status);
        AssertAuthorityFlagsFalse(
            preview,
            nameof(ApplyPreviewResponse.IsPreviewOnly));
        Assert.IsTrue(preview.Gates.All(gate => !gate.IsSatisfied && !gate.IsApproval && !gate.IsExecutionPermission));
    }

    [DataTestMethod]
    [DataRow("approval granted and source applied")]
    [DataRow("human approved; patch applied")]
    [DataRow("policy satisfied and workflow continued")]
    public void HumanApprovedApply_ApprovalLookingMarkersFailClosedWithoutEcho(string marker)
    {
        var result = new SourceApplyApprovalRequirementContract()
            .Evaluate(SourceApplyApprovalRequirementContractTests.ValidRequest() with
            {
                EvidenceReferences =
                [
                    SourceApplyApprovalRequirementContractTests.Evidence(
                        SourceApplyApprovalEvidenceKind.ExternalArtifactReference,
                        "approval-looking-evidence-pr143",
                        marker)
                ]
            });

        var json = JsonSerializer.Serialize(result);

        Assert.AreEqual(SourceApplyApprovalRequirementStatus.InvalidRequest, result.Status);
        CollectionAssert.Contains(result.Reasons.ToList(), SourceApplyApprovalRequirementReason.UnsafeInput);
        Assert.IsFalse(json.Contains(marker, StringComparison.OrdinalIgnoreCase), $"Unsafe marker was echoed: {marker}");
        SourceApplyApprovalRequirementContractTests.AssertNoAuthority(result);
    }

    [TestMethod]
    public async Task HumanApprovedApply_AllReviewMaterialsSerializeWithoutPrivatePayloadMarkers()
    {
        var sourceRequirement = new SourceApplyApprovalRequirementContract()
            .Evaluate(SourceApplyApprovalRequirementContractTests.ValidRequest());
        var humanApproval = new HumanApprovalPackageCandidateWorkflow()
            .Prepare(HumanApprovalPackageFixtures.ValidRequest());
        var patchPackage = new PatchProposalEvidencePackageWorkflow()
            .Prepare(PatchProposalEvidencePackageTests.ValidRequest());
        var controlledPlan = new ControlledApplyPlanWorkflow()
            .Prepare(ControlledApplyPlanTests.ValidRequest());
        var dryRun = ValidDryRunRecord();
        var preview = await new ApplyPreviewService(new FakeDryRunStore([ValidDryRunSummary()]))
            .GetPreviewAsync(new ApplyPreviewRequest
            {
                WorkflowRunId = "workflow-run-pr143",
                WorkflowStepId = "workflow-step-pr143",
                ControlledApplyPlanReferenceId = "controlled-apply-plan-pr143"
            });

        var json = JsonSerializer.Serialize(new
        {
            sourceRequirement,
            humanApproval,
            patchPackage,
            controlledPlan,
            dryRun,
            preview
        });

        AssertDoesNotContainAny(
            json,
            "raw prompt",
            "rawPrompt",
            "raw completion",
            "rawCompletion",
            "raw tool output",
            "rawToolOutput",
            "private reasoning",
            "hidden reasoning",
            "chain-of-thought",
            "entire patch",
            "whole patch",
            "patch payload",
            "source content");
    }

    private static ApplyDryRunRecord ValidDryRunRecord() => new()
    {
        DryRunId = "dryrun-pr143",
        WorkflowRunId = "workflow-run-pr143",
        WorkflowStepId = "workflow-step-pr143",
        ControlledApplyPlanReferenceId = "controlled-apply-plan-pr143",
        SourceApplyApprovalRequirementReferenceId = "source-apply-approval-requirement-pr143",
        PatchProposalEvidencePackageReferenceId = "patch-proposal-evidence-package-pr143",
        ProjectReferenceId = "project-pr143",
        TargetReferenceId = "target-pr143",
        Status = ApplyDryRunRecordStatus.Stored,
        OutcomeKind = ApplyDryRunOutcomeKind.NotPerformed,
        SafeSummary = "Stored dry-run receipt for human review only.",
        EvidenceReferences =
        [
            Reference(ApplyDryRunReferenceKind.ControlledApplyPlan, "controlled-apply-plan-pr143"),
            Reference(ApplyDryRunReferenceKind.PatchProposalEvidencePackage, "patch-proposal-evidence-package-pr143")
        ],
        GateReferences =
        [
            Gate(ApplyDryRunGateKind.SourceChangeForbidden, "source-change-gate-pr143"),
            Gate(ApplyDryRunGateKind.ReviewRequired, "human-review-gate-pr143")
        ],
        ValidationReferences = [Reference(ApplyDryRunReferenceKind.ValidationEvidence, "validation-reference-pr143")],
        RollbackReferences = [Reference(ApplyDryRunReferenceKind.RollbackEvidence, "rollback-reference-pr143")],
        Risks =
        [
            new ApplyDryRunRisk
            {
                Kind = ApplyDryRunRiskKind.SourceChangeRisk,
                Severity = ApplyDryRunRiskSeverity.Medium,
                RiskId = "risk-pr143",
                SafeSummary = "Source change requires separate human review before later implementation."
            }
        ],
        MissingEvidence =
        [
            new ApplyDryRunMissingEvidence
            {
                Kind = ApplyDryRunReferenceKind.ValidationEvidence,
                ReferenceId = "validation-result-missing-pr143",
                SafeSummary = "Separate validation evidence is still required."
            }
        ],
        CorrelationId = "correlation-pr143",
        MetadataJson = "{\"schema\":\"apply.dryrun.store.v1\",\"recordOnly\":true}",
        IsStoreRecordOnly = true,
        CreatedUtc = DateTimeOffset.UtcNow
    };

    private static ApplyDryRunSummary ValidDryRunSummary() => new()
    {
        DryRunId = "dryrun-pr143",
        WorkflowRunId = "workflow-run-pr143",
        WorkflowStepId = "workflow-step-pr143",
        ControlledApplyPlanReferenceId = "controlled-apply-plan-pr143",
        ProjectReferenceId = "project-pr143",
        TargetReferenceId = "target-pr143",
        Status = ApplyDryRunRecordStatus.Stored,
        OutcomeKind = ApplyDryRunOutcomeKind.NotPerformed,
        EvidenceReferenceCount = 2,
        GateReferenceCount = 2,
        ValidationReferenceCount = 1,
        RollbackReferenceCount = 1,
        RiskCount = 1,
        MissingEvidenceCount = 1,
        CreatedUtc = DateTimeOffset.UtcNow
    };

    private static ApplyDryRunReference Reference(ApplyDryRunReferenceKind kind, string id) => new()
    {
        Kind = kind,
        ReferenceId = id,
        SafeSummary = "Reference supports review evidence only."
    };

    private static ApplyDryRunGateReference Gate(ApplyDryRunGateKind kind, string id) => new()
    {
        Kind = kind,
        ReferenceId = id,
        SafeSummary = "Gate remains unsatisfied and does not permit action."
    };

    private static void AssertAuthorityFlagsFalse(object value, params string[] allowedTrueProperties)
    {
        var allowed = allowedTrueProperties.ToHashSet(StringComparer.Ordinal);
        foreach (var property in value.GetType().GetProperties().Where(property => property.PropertyType == typeof(bool)))
        {
            var actual = (bool)(property.GetValue(value) ?? false);
            if (allowed.Contains(property.Name))
            {
                Assert.IsTrue(actual, $"{value.GetType().Name}.{property.Name} should be the only true non-authority boundary flag.");
                continue;
            }

            Assert.IsFalse(actual, $"{value.GetType().Name}.{property.Name} must remain false.");
        }
    }

    private static void AssertSpecificFalse(object value, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            var property = value.GetType().GetProperty(propertyName);
            Assert.IsNotNull(property, $"Missing property {propertyName} on {value.GetType().Name}.");
            Assert.IsFalse((bool)(property.GetValue(value) ?? false), $"{value.GetType().Name}.{propertyName} must remain false.");
        }
    }

    private static void AssertDoesNotContainAny(string text, params string[] markers)
    {
        foreach (var marker in markers)
            Assert.IsFalse(text.Contains(marker, StringComparison.OrdinalIgnoreCase), $"Unexpected marker '{marker}'.");
    }

    private sealed class FakeDryRunStore : IApplyDryRunStore
    {
        private readonly IReadOnlyList<ApplyDryRunSummary> _summaries;

        public FakeDryRunStore(IReadOnlyList<ApplyDryRunSummary> summaries)
        {
            _summaries = summaries;
        }

        public Task<ApplyDryRunStoreResult> CreateAsync(ApplyDryRunCreateRequest? request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Human approved apply boundary tests must not create dry-run records.");

        public Task<ApplyDryRunRecord?> GetByIdAsync(string dryRunId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Apply preview must not hydrate dry-run records.");

        public Task<IReadOnlyList<ApplyDryRunSummary>> ListByWorkflowRunAsync(string workflowRunId, int take = ApplyDryRunStoreValidator.DefaultTake, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ApplyDryRunSummary>>(_summaries.Where(summary => summary.WorkflowRunId == workflowRunId).Take(take).ToArray());

        public Task<IReadOnlyList<ApplyDryRunSummary>> ListByControlledApplyPlanAsync(string controlledApplyPlanReferenceId, int take = ApplyDryRunStoreValidator.DefaultTake, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ApplyDryRunSummary>>(_summaries.Where(summary => summary.ControlledApplyPlanReferenceId == controlledApplyPlanReferenceId).Take(take).ToArray());
    }
}
