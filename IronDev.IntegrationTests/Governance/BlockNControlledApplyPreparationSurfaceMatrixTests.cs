using IronDev.Core.Workflow;
using IronDev.Infrastructure.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("BlockNControlledApplyPreparation")]
public sealed class BlockNControlledApplyPreparationSurfaceMatrixTests
{
    [TestMethod]
    public async Task BlockNResultModels_ExposeNoAuthorityFlags()
    {
        var models = new object[]
        {
            new SourceApplyApprovalRequirementContract().Evaluate(SourceApplyApprovalRequirementContractTests.ValidRequest()),
            new PatchProposalEvidencePackageWorkflow().Prepare(PatchProposalEvidencePackageTests.ValidRequest()),
            new ControlledApplyPlanWorkflow().Prepare(ControlledApplyPlanTests.ValidRequest()),
            ValidDryRunRecord(),
            await new ApplyPreviewService(new FakeDryRunStore([ValidDryRunSummary()])).GetPreviewAsync(new ApplyPreviewRequest
            {
                WorkflowRunId = "workflow-run-pr144",
                WorkflowStepId = "workflow-step-pr144",
                ControlledApplyPlanReferenceId = "controlled-apply-plan-pr144"
            })
        };

        foreach (var model in models)
        {
            AssertAuthorityFlagsFalse(model);
            AssertHasPostureFlag(model);
        }
    }

    private static void AssertAuthorityFlagsFalse(object model)
    {
        foreach (var propertyName in new[]
        {
            "IsApproval",
            "IsApproved",
            "IsApprovalSatisfied",
            "IsPolicySatisfied",
            "IsSourceApply",
            "IsPatchApply",
            "IsPatchApplication",
            "IsDryRunExecution",
            "IsDryRunPerformed",
            "IsExecution",
            "CanApplySource",
            "CanApplyPatch",
            "CanMutateFiles",
            "CanReadSourceFiles",
            "CanExecuteDryRun",
            "CanPerformDryRun",
            "CanRunCommand",
            "CanInvokeTool",
            "CanDispatchAgent",
            "CanCallModel",
            "CanBuildPrompt",
            "CanRunValidation",
            "CanRollback",
            "CanSatisfyApproval",
            "CanSatisfyPolicy",
            "CanTransitionWorkflow",
            "CanCreateTicket",
            "CanPromoteMemory",
            "CanActivateRetrieval"
        })
        {
            var property = model.GetType().GetProperty(propertyName);
            if (property is null)
                continue;

            Assert.IsFalse((bool)(property.GetValue(model) ?? false), $"{model.GetType().Name}.{propertyName} must remain false.");
        }
    }

    private static void AssertHasPostureFlag(object model)
    {
        foreach (var propertyName in new[]
        {
            "IsRequirementOnly",
            "IsPackageOnly",
            "IsPlanOnly",
            "IsStoreRecordOnly",
            "IsPreviewOnly"
        })
        {
            var property = model.GetType().GetProperty(propertyName);
            if (property is not null)
            {
                Assert.IsTrue((bool)(property.GetValue(model) ?? false), $"{model.GetType().Name}.{propertyName} should be true.");
                return;
            }
        }

        Assert.Fail($"{model.GetType().Name} has no recognized Block N posture flag.");
    }

    private static ApplyDryRunRecord ValidDryRunRecord() => new()
    {
        DryRunId = "dryrun-pr144",
        WorkflowRunId = "workflow-run-pr144",
        WorkflowStepId = "workflow-step-pr144",
        ControlledApplyPlanReferenceId = "controlled-apply-plan-pr144",
        SourceApplyApprovalRequirementReferenceId = "source-apply-approval-requirement-pr144",
        PatchProposalEvidencePackageReferenceId = "patch-proposal-evidence-package-pr144",
        ProjectReferenceId = "project-pr144",
        TargetReferenceId = "target-pr144",
        Status = ApplyDryRunRecordStatus.Stored,
        OutcomeKind = ApplyDryRunOutcomeKind.NotPerformed,
        SafeSummary = "Stored dry-run receipt for human review only.",
        EvidenceReferences =
        [
            Reference(ApplyDryRunReferenceKind.ControlledApplyPlan, "controlled-apply-plan-pr144"),
            Reference(ApplyDryRunReferenceKind.PatchProposalEvidencePackage, "patch-proposal-evidence-package-pr144")
        ],
        GateReferences =
        [
            Gate(ApplyDryRunGateKind.SourceChangeForbidden, "source-change-gate-pr144"),
            Gate(ApplyDryRunGateKind.ReviewRequired, "human-review-gate-pr144")
        ],
        ValidationReferences = [Reference(ApplyDryRunReferenceKind.ValidationEvidence, "validation-reference-pr144")],
        RollbackReferences = [Reference(ApplyDryRunReferenceKind.RollbackEvidence, "rollback-reference-pr144")],
        Risks =
        [
            new ApplyDryRunRisk
            {
                Kind = ApplyDryRunRiskKind.SourceChangeRisk,
                Severity = ApplyDryRunRiskSeverity.Medium,
                RiskId = "risk-pr144",
                SafeSummary = "Source change requires separate human review before later implementation."
            }
        ],
        MissingEvidence =
        [
            new ApplyDryRunMissingEvidence
            {
                Kind = ApplyDryRunReferenceKind.ValidationEvidence,
                ReferenceId = "validation-result-missing-pr144",
                SafeSummary = "Separate validation evidence is still required."
            }
        ],
        CorrelationId = "correlation-pr144",
        MetadataJson = "{\"schema\":\"apply.dryrun.store.v1\",\"recordOnly\":true}",
        IsStoreRecordOnly = true,
        CreatedUtc = DateTimeOffset.UtcNow
    };

    private static ApplyDryRunSummary ValidDryRunSummary() => new()
    {
        DryRunId = "dryrun-pr144",
        WorkflowRunId = "workflow-run-pr144",
        WorkflowStepId = "workflow-step-pr144",
        ControlledApplyPlanReferenceId = "controlled-apply-plan-pr144",
        ProjectReferenceId = "project-pr144",
        TargetReferenceId = "target-pr144",
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

    private sealed class FakeDryRunStore : IApplyDryRunStore
    {
        private readonly IReadOnlyList<ApplyDryRunSummary> _summaries;

        public FakeDryRunStore(IReadOnlyList<ApplyDryRunSummary> summaries)
        {
            _summaries = summaries;
        }

        public Task<ApplyDryRunStoreResult> CreateAsync(ApplyDryRunCreateRequest? request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Block N receipt matrix tests must not create dry-run records.");

        public Task<ApplyDryRunRecord?> GetByIdAsync(string dryRunId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Block N receipt matrix tests must not hydrate dry-run records.");

        public Task<IReadOnlyList<ApplyDryRunSummary>> ListByWorkflowRunAsync(string workflowRunId, int take = ApplyDryRunStoreValidator.DefaultTake, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ApplyDryRunSummary>>(_summaries.Where(summary => summary.WorkflowRunId == workflowRunId).Take(take).ToArray());

        public Task<IReadOnlyList<ApplyDryRunSummary>> ListByControlledApplyPlanAsync(string controlledApplyPlanReferenceId, int take = ApplyDryRunStoreValidator.DefaultTake, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ApplyDryRunSummary>>(_summaries.Where(summary => summary.ControlledApplyPlanReferenceId == controlledApplyPlanReferenceId).Take(take).ToArray());
    }
}
