using IronDev.Core.Validation;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockBK0ValidationLanePlannerTests
{
    [TestMethod]
    public void BlockBK0_Planner_SelectsFocusedLanesForChangedFiles()
    {
        var plan = new ValidationLanePlanner().Plan(new ValidationLanePlanRequest
        {
            BaseRef = "main",
            HeadRef = "bk0/validation-runtime-hardening",
            CurrentBlock = "BK0",
            ChangedFiles =
            [
                "IronDev.Core/Governance/GovernedActionKernel.cs",
                "tools/IronDev.Cli/CliValidation.cs",
                "IronDev.Core/Validation/ValidationLanePlanner.cs",
                "IronDev.IntegrationTests/BlockAOMergeAndReleaseSeparationTests.cs",
                "IronDev.Core/IronDev.Core.csproj"
            ]
        });

        AssertLane(plan, "restore");
        AssertLane(plan, "build");
        AssertLane(plan, "cli-command-surface");
        AssertLane(plan, "impacted-governance-tests");
        AssertLane(plan, "focused-ao");
        AssertLane(plan, "focused-bk0");
        AssertLane(plan, "fast-authority-invariants");
        Assert.IsTrue(plan.ValidationPlanId.StartsWith("validation_plan_", StringComparison.Ordinal));
        Assert.IsTrue(plan.Boundary.EvidenceOnly);
    }

    [TestMethod]
    public void BlockBK0_Planner_KeepsReceiptOnlyChangesFocused()
    {
        var plan = new ValidationLanePlanner().Plan(new ValidationLanePlanRequest
        {
            ChangedFiles = ["Docs/receipts/BK0_VALIDATION_RUNTIME_AND_TEST_HARNESS_HARDENING.md"]
        });

        AssertLane(plan, "diff-check");
        AssertLane(plan, "docs-receipt-check");
        AssertLane(plan, "fast-authority-invariants");
        Assert.IsFalse(plan.Lanes.Any(lane => string.Equals(lane.Name, "build", StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(plan.Lanes.Any(lane => string.Equals(lane.Name, "phase-gate", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void BlockBK0_Planner_AddsPhaseGateOnlyWhenPhaseBoundaryIsPresent()
    {
        var noPhase = new ValidationLanePlanner().Plan(new ValidationLanePlanRequest
        {
            ChangedFiles = ["IronDev.Core/Validation/ValidationLanePlanner.cs"]
        });
        var phase = new ValidationLanePlanner().Plan(new ValidationLanePlanRequest
        {
            Phase = "governance-hardening",
            ChangedFiles = ["IronDev.Core/Validation/ValidationLanePlanner.cs"]
        });

        Assert.IsFalse(noPhase.Lanes.Any(lane => string.Equals(lane.Name, "phase-gate", StringComparison.OrdinalIgnoreCase)));
        AssertLane(phase, "phase-gate");
    }

    [TestMethod]
    public void BlockBK0_CachePolicy_RejectsAuthorityAndMutationCachedPassEvidence()
    {
        var policy = new ValidationCachePolicy();

        foreach (var category in new[] { "authority", "source-apply", "rollback", "workflow", "memory-promotion", "cli-mutation", "db", "dogfood", "release", "merge" })
        {
            Assert.IsFalse(ValidationCachePolicyEvaluator.CanAcceptCachedPassEvidence(category, policy), category);
            Assert.AreEqual(ValidationFailureKind.CachePolicyViolation, ValidationCachePolicyEvaluator.ClassifyCachedPassEvidence(category, policy), category);
        }

        Assert.IsTrue(ValidationCachePolicyEvaluator.CanAcceptCachedPassEvidence("docs", policy));
        Assert.AreEqual(ValidationFailureKind.Passed, ValidationCachePolicyEvaluator.ClassifyCachedPassEvidence("docs", policy));
    }

    private static void AssertLane(ValidationLanePlan plan, string laneName) =>
        Assert.IsTrue(plan.Lanes.Any(lane => string.Equals(lane.Name, laneName, StringComparison.OrdinalIgnoreCase)), laneName);
}
