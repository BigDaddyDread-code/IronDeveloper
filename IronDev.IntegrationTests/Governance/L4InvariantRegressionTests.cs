using IronDev.Core.Governance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("L4InvariantRegression")]
public sealed class L4InvariantRegressionTests
{
    private static readonly string[] CoreInvariantMaxims =
    [
        "Capability matrix is not capability execution.",
        "Capability definition is not authority.",
        "Matrix row is not permission.",
        "Evidence requirement is not evidence."
    ];

    [TestMethod]
    public void L4Invariants_DefinitionIsNeverAuthority()
    {
        foreach (var entry in Entries())
        {
            CollectionAssert.Contains(entry.BoundaryMaxims.ToArray(), "Capability matrix is not capability execution.", entry.CapabilityCode);
            CollectionAssert.Contains(entry.BoundaryMaxims.ToArray(), "Capability definition is not authority.", entry.CapabilityCode);
            CollectionAssert.Contains(entry.BoundaryMaxims.ToArray(), "Matrix row is not permission.", entry.CapabilityCode);
            CollectionAssert.AreEqual(new[] { "definition only" }, entry.AllowedEffects.ToArray(), entry.CapabilityCode);

            foreach (var forbiddenAllowedEffect in ForbiddenAllowedEffects())
            {
                Assert.IsFalse(
                    entry.AllowedEffects.Any(effect => effect.Contains(forbiddenAllowedEffect, StringComparison.OrdinalIgnoreCase)),
                    $"{entry.CapabilityCode} allowed effects must not include '{forbiddenAllowedEffect}'.");
            }
        }
    }

    [TestMethod]
    public void L4Invariants_RequirementIsNeverEvidence()
    {
        foreach (var entry in Entries().Where(entry => entry.RequiredEvidenceRecords.Count > 0))
        {
            CollectionAssert.Contains(entry.BoundaryMaxims.ToArray(), "Evidence requirement is not evidence.", entry.CapabilityCode);

            foreach (var requiredEvidence in entry.RequiredEvidenceRecords)
            {
                Assert.IsFalse(
                    entry.AllowedEffects.Any(effect => effect.Equals(requiredEvidence, StringComparison.OrdinalIgnoreCase)),
                    $"{entry.CapabilityCode} must not name required evidence '{requiredEvidence}' as an allowed effect.");
            }
        }
    }

    [TestMethod]
    public void L4Invariants_ApprovalRequirementIsNotAcceptedApproval()
    {
        var approval = Matrix().Get(L4CapabilityCodes.AcceptedApprovalRecord);

        CollectionAssert.Contains(approval.RequiredAuthorityRecords.ToArray(), "human approval evidence");
        AssertForbidden(approval,
            "create approval",
            "accept approval",
            "approve workflow",
            "satisfy policy",
            "continue workflow",
            "apply source",
            "release software");
    }

    [TestMethod]
    public void L4Invariants_PolicyRequirementIsNotPolicySatisfaction()
    {
        var policy = Matrix().Get(L4CapabilityCodes.PolicySatisfactionRecord);

        Assert.IsFalse(policy.Implemented);
        AssertForbidden(policy,
            "satisfy policy",
            "override policy",
            "continue workflow",
            "apply source",
            "release software");
    }

    [TestMethod]
    public void L4Invariants_DryRunRequirementIsNotDryRunExecution()
    {
        var dryRun = Matrix().Get(L4CapabilityCodes.ControlledDryRun);

        Assert.IsFalse(dryRun.Implemented);
        AssertForbidden(dryRun,
            "run dry-run",
            "apply patch",
            "mutate source",
            "continue workflow",
            "release software");
    }

    [TestMethod]
    public void L4Invariants_PatchRequirementIsNotPatchArtifactCreation()
    {
        var patch = Matrix().Get(L4CapabilityCodes.PatchArtifact);

        Assert.IsFalse(patch.Implemented);
        AssertForbidden(patch,
            "create patch artifact",
            "apply patch",
            "mutate source",
            "continue workflow",
            "release software");
    }

    [TestMethod]
    public void L4Invariants_SourceApplyRequirementIsNotSourceApply()
    {
        var sourceApply = Matrix().Get(L4CapabilityCodes.ControlledSourceApply);

        Assert.IsFalse(sourceApply.Implemented);
        AssertRequiresAuthority(sourceApply,
            "accepted approval record",
            "policy satisfaction record",
            "controlled dry-run result",
            "patch artifact",
            "rollback plan",
            "source apply approval requirement");
        AssertForbidden(sourceApply,
            "apply source",
            "write files",
            "commit changes",
            "push branch",
            "continue workflow",
            "release software");
    }

    [TestMethod]
    public void L4Invariants_RollbackRequirementIsNotRollbackExecution()
    {
        var rollback = Matrix().Get(L4CapabilityCodes.RollbackRecord);

        Assert.IsFalse(rollback.Implemented);
        AssertForbidden(rollback,
            "execute rollback",
            "mutate source",
            "continue workflow",
            "release software");
    }

    [TestMethod]
    public void L4Invariants_WorkflowContinuationRequirementIsNotContinuation()
    {
        var continuation = Matrix().Get(L4CapabilityCodes.WorkflowContinuation);

        Assert.IsFalse(continuation.Implemented);
        AssertRequiresAuthority(continuation,
            "policy satisfaction record",
            "source apply record or explicit no-apply proof",
            "validation proof",
            "workflow transition decision");
        AssertForbidden(continuation,
            "continue workflow",
            "transition workflow",
            "retry workflow",
            "repair workflow",
            "release software");
    }

    [TestMethod]
    public void L4Invariants_ReleaseReadinessRequirementIsNotReleaseReadiness()
    {
        var release = Matrix().Get(L4CapabilityCodes.ReleaseReadinessGate);

        Assert.IsFalse(release.Implemented);
        CollectionAssert.Contains(release.BoundaryMaxims.ToArray(), "Dogfood pass is not release readiness.");
        CollectionAssert.Contains(release.BoundaryMaxims.ToArray(), "Health check is not release readiness.");
        CollectionAssert.Contains(release.BoundaryMaxims.ToArray(), "Validation summary is not release readiness.");
        CollectionAssert.Contains(release.BoundaryMaxims.ToArray(), "UI review is not release readiness.");
        AssertForbidden(release,
            "approve release",
            "mark release ready",
            "ship software",
            "tag release",
            "deploy");
    }

    [TestMethod]
    public void L4Invariants_UiCannotOwnAnyL4Authority()
    {
        foreach (var entry in Entries())
        {
            CollectionAssert.Contains(entry.BoundaryMaxims.ToArray(), "UI cannot own L4 authority.", entry.CapabilityCode);
        }

        StringAssert.Contains(ReceiptText(), "Backend authority must be backend-owned.");
    }

    [TestMethod]
    public void L4Invariants_AllL4CapabilitiesRemainDefinitionOnly()
    {
        foreach (var entry in Entries())
        {
            Assert.IsFalse(entry.Implemented, $"{entry.CapabilityCode} must remain unimplemented.");
            CollectionAssert.AreEqual(new[] { "definition only" }, entry.AllowedEffects.ToArray(), entry.CapabilityCode);
        }
    }

    [TestMethod]
    public void L4Invariants_NoMutationServicesReferenced()
    {
        var root = RepositoryRoot();
        var files = new[]
        {
            Path.Combine(root, "IronDev.Core", "Governance", "L4CapabilityCodes.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "L4CapabilityMatrix.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "L4CapabilityMatrixEntry.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "IL4CapabilityMatrix.cs"),
            Path.Combine(root, "Docs", "receipts", "PR161_L4_CAPABILITY_MATRIX.md"),
            Path.Combine(root, "Docs", "receipts", "PR162_L4_INVARIANT_REGRESSION_SUITE.md"),
            Path.Combine(root, "IronDev.IntegrationTests", "Governance", "L4InvariantRegressionTests.cs")
        };

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var token in ForbiddenStaticTokens())
            {
                Assert.IsFalse(text.Contains(token, StringComparison.Ordinal), $"{Path.GetFileName(file)} must not reference {token}.");
            }
        }
    }

    [TestMethod]
    public void L4Invariants_ReceiptStatesRegressionPurpose()
    {
        var receipt = ReceiptText();

        StringAssert.Contains(receipt, "PR162 adds the L4 invariant regression suite.");
        StringAssert.Contains(receipt, "This PR is tests/receipt only.");
        foreach (var maxim in RequiredReceiptMaxims())
        {
            StringAssert.Contains(receipt, maxim);
        }

        StringAssert.Contains(
            receipt,
            "PR162 does not implement accepted approval records, policy satisfaction records, dry-run execution, patch artifacts, source apply, rollback, workflow continuation, or release readiness.");
        StringAssert.Contains(receipt, "PR162 nails down the L4 invariants. It does not activate L4.");
    }

    private static L4CapabilityMatrix Matrix() => new();

    private static IReadOnlyList<L4CapabilityMatrixEntry> Entries() => Matrix().List();

    private static void AssertRequiresAuthority(L4CapabilityMatrixEntry entry, params string[] requiredAuthorityRecords)
    {
        foreach (var requiredAuthority in requiredAuthorityRecords)
        {
            CollectionAssert.Contains(entry.RequiredAuthorityRecords.ToArray(), requiredAuthority, entry.CapabilityCode);
        }
    }

    private static void AssertForbidden(L4CapabilityMatrixEntry entry, params string[] forbiddenEffects)
    {
        foreach (var forbiddenEffect in forbiddenEffects)
        {
            CollectionAssert.Contains(entry.ForbiddenEffects.ToArray(), forbiddenEffect, entry.CapabilityCode);
        }
    }

    private static IReadOnlyList<string> ForbiddenAllowedEffects() =>
    [
        "approve",
        "satisfy policy",
        "run dry-run",
        "create patch",
        "apply source",
        "execute rollback",
        "continue workflow",
        "transition workflow",
        "approve release",
        "release software",
        "deploy"
    ];

    private static IReadOnlyList<string> RequiredReceiptMaxims() =>
    [
        "Capability matrix is not capability execution.",
        "Capability definition is not authority.",
        "Matrix row is not permission.",
        "Evidence requirement is not evidence.",
        "Required approval is not accepted approval.",
        "Required policy is not policy satisfaction.",
        "Required dry-run is not dry-run execution.",
        "Required patch artifact is not a patch artifact.",
        "Required source apply is not source apply.",
        "Required rollback is not rollback.",
        "Required workflow continuation is not workflow continuation.",
        "Required release gate is not release readiness.",
        "UI cannot own L4 authority.",
        "Backend authority must be backend-owned.",
        "Dogfood pass is not release readiness.",
        "Health check is not release readiness.",
        "Validation summary is not release readiness.",
        "UI review is not release readiness."
    ];

    private static IReadOnlyList<string> ForbiddenStaticTokens() =>
    [
        "Source" + "Writer",
        "Patch" + "Writer",
        "Apply" + "Source",
        "Apply" + "Patch",
        "Workflow" + "Runner",
        "Workflow" + "Dispatcher",
        "Tool" + "Executor",
        "Tool" + "Invoker",
        "Agent" + "Dispatcher",
        "Release" + "Publisher",
        "Memory" + "Promotion",
        "Retrieval" + "Activation",
        "Sql" + "Connection",
        "Db" + "Command",
        "File." + "Write",
        "File." + "Delete",
        "Process." + "Start",
        "git " + "commit",
        "git " + "push"
    ];

    private static string ReceiptText() => File.ReadAllText(ReceiptPath());

    private static string ReceiptPath() =>
        Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR162_L4_INVARIANT_REGRESSION_SUITE.md");

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "IronDev.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root containing IronDev.slnx.");
    }
}
