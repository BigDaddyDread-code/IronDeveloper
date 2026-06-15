using IronDev.Core.Governance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("L4CapabilityMatrix")]
public sealed class L4CapabilityMatrixTests
{
    private static readonly string[] RequiredCodes =
    [
        L4CapabilityCodes.AcceptedApprovalRecord,
        L4CapabilityCodes.PolicySatisfactionRecord,
        L4CapabilityCodes.ControlledDryRun,
        L4CapabilityCodes.PatchArtifact,
        L4CapabilityCodes.ControlledSourceApply,
        L4CapabilityCodes.RollbackRecord,
        L4CapabilityCodes.WorkflowContinuation,
        L4CapabilityCodes.ReleaseReadinessGate
    ];

    [TestMethod]
    public void L4CapabilityMatrix_ContainsRequiredCapabilities()
    {
        var matrix = new L4CapabilityMatrix();
        var codes = matrix.List().Select(entry => entry.CapabilityCode).ToArray();

        CollectionAssert.AreEquivalent(RequiredCodes, codes);
    }

    [TestMethod]
    public void L4CapabilityMatrix_IsOrderedByBackendAuthorityChain()
    {
        var matrix = new L4CapabilityMatrix();
        var orderedCodes = matrix.List().OrderBy(entry => entry.Order).Select(entry => entry.CapabilityCode).ToArray();

        CollectionAssert.AreEqual(RequiredCodes, orderedCodes);
    }

    [TestMethod]
    public void L4CapabilityMatrix_DoesNotMarkExecutionCapabilitiesImplemented()
    {
        var matrix = new L4CapabilityMatrix();

        foreach (var entry in matrix.List())
        {
            Assert.IsFalse(entry.Implemented, $"{entry.CapabilityCode} must remain unimplemented in PR161.");
            CollectionAssert.AreEqual(new[] { "definition only" }, entry.AllowedEffects.ToArray(), entry.CapabilityCode);
        }
    }

    [TestMethod]
    public void L4CapabilityMatrix_DefinitionDoesNotGrantAuthority()
    {
        var matrix = new L4CapabilityMatrix();
        var requiredMaxims = new[]
        {
            "Capability matrix is not capability execution.",
            "Capability definition is not authority.",
            "Matrix row is not permission.",
            "Evidence requirement is not evidence."
        };

        foreach (var entry in matrix.List())
        {
            foreach (var maxim in requiredMaxims)
            {
                CollectionAssert.Contains(entry.BoundaryMaxims.ToArray(), maxim, entry.CapabilityCode);
            }
        }
    }

    [TestMethod]
    public void L4CapabilityMatrix_SourceApplyRequiresPriorAuthority()
    {
        var sourceApply = new L4CapabilityMatrix().Get(L4CapabilityCodes.ControlledSourceApply);

        CollectionAssert.Contains(sourceApply.RequiredAuthorityRecords.ToArray(), "accepted approval record");
        CollectionAssert.Contains(sourceApply.RequiredAuthorityRecords.ToArray(), "policy satisfaction record");
        CollectionAssert.Contains(sourceApply.RequiredAuthorityRecords.ToArray(), "controlled dry-run result");
        CollectionAssert.Contains(sourceApply.RequiredAuthorityRecords.ToArray(), "patch artifact");
        CollectionAssert.Contains(sourceApply.RequiredAuthorityRecords.ToArray(), "rollback plan");
        CollectionAssert.Contains(sourceApply.ForbiddenEffects.ToArray(), "apply source");
        CollectionAssert.Contains(sourceApply.ForbiddenEffects.ToArray(), "write files");
    }

    [TestMethod]
    public void L4CapabilityMatrix_WorkflowContinuationRequiresBackendTransitionDecision()
    {
        var continuation = new L4CapabilityMatrix().Get(L4CapabilityCodes.WorkflowContinuation);

        CollectionAssert.Contains(continuation.RequiredAuthorityRecords.ToArray(), "policy satisfaction record");
        CollectionAssert.Contains(continuation.RequiredAuthorityRecords.ToArray(), "validation proof");
        CollectionAssert.Contains(continuation.RequiredAuthorityRecords.ToArray(), "workflow transition decision");
        CollectionAssert.Contains(continuation.ForbiddenEffects.ToArray(), "continue workflow");
        CollectionAssert.Contains(continuation.ForbiddenEffects.ToArray(), "transition workflow");
    }

    [TestMethod]
    public void L4CapabilityMatrix_ReleaseReadinessIsNotDogfoodPass()
    {
        var release = new L4CapabilityMatrix().Get(L4CapabilityCodes.ReleaseReadinessGate);

        CollectionAssert.Contains(release.BoundaryMaxims.ToArray(), "Dogfood pass is not release readiness.");
        CollectionAssert.Contains(release.BoundaryMaxims.ToArray(), "Health check is not release readiness.");
        CollectionAssert.Contains(release.BoundaryMaxims.ToArray(), "Validation summary is not release readiness.");
        CollectionAssert.Contains(release.BoundaryMaxims.ToArray(), "UI review is not release readiness.");
        CollectionAssert.Contains(release.ForbiddenEffects.ToArray(), "approve release");
        CollectionAssert.Contains(release.ForbiddenEffects.ToArray(), "mark release ready");
    }

    [TestMethod]
    public void L4CapabilityMatrix_UiCannotOwnAnyL4Capability()
    {
        var matrix = new L4CapabilityMatrix();

        foreach (var entry in matrix.List())
        {
            CollectionAssert.Contains(entry.BoundaryMaxims.ToArray(), "UI cannot own L4 authority.", entry.CapabilityCode);
        }
    }

    [TestMethod]
    public void L4CapabilityMatrix_DoesNotReferenceMutationServices()
    {
        var root = RepositoryRoot();
        var files = new[]
        {
            Path.Combine(root, "IronDev.Core", "Governance", "L4CapabilityCodes.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "L4CapabilityMatrix.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "L4CapabilityMatrixEntry.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "IL4CapabilityMatrix.cs"),
            Path.Combine(root, "Docs", "receipts", "PR161_L4_CAPABILITY_MATRIX.md")
        };

        var forbiddenTokens = new[]
        {
            "SourceWriter",
            "PatchWriter",
            "ApplySource",
            "ApplyPatch",
            "WorkflowRunner",
            "ToolExecutor",
            "AgentDispatcher",
            "ReleasePublisher",
            "MemoryPromotion",
            "RetrievalActivation"
        };

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var token in forbiddenTokens)
            {
                Assert.IsFalse(text.Contains(token, StringComparison.Ordinal), $"{Path.GetFileName(file)} must not reference {token}.");
            }
        }
    }

    [TestMethod]
    public void L4CapabilityMatrix_ReceiptStatesBoundary()
    {
        var receipt = File.ReadAllText(ReceiptPath());

        StringAssert.Contains(receipt, "PR161 defines the L4 capability matrix.");
        StringAssert.Contains(receipt, "Capability matrix is not capability execution.");
        StringAssert.Contains(receipt, "Capability definition is not authority.");
        StringAssert.Contains(receipt, "Matrix row is not permission.");
        StringAssert.Contains(receipt, "Evidence requirement is not evidence.");
        StringAssert.Contains(receipt, "Required approval is not accepted approval.");
        StringAssert.Contains(receipt, "Required policy is not policy satisfaction.");
        StringAssert.Contains(receipt, "Required dry-run is not dry-run execution.");
        StringAssert.Contains(receipt, "Required patch artifact is not a patch artifact.");
        StringAssert.Contains(receipt, "Required source apply is not source apply.");
        StringAssert.Contains(receipt, "Required rollback is not rollback.");
        StringAssert.Contains(receipt, "Required workflow continuation is not workflow continuation.");
        StringAssert.Contains(receipt, "Required release gate is not release readiness.");
        StringAssert.Contains(receipt, "Backend authority must be backend-owned.");
        StringAssert.Contains(receipt, "UI cannot own L4 authority.");
        StringAssert.Contains(receipt, "accepted approval record -> policy satisfaction record -> controlled dry-run -> patch artifact -> controlled source apply -> rollback -> workflow continuation -> release readiness gate");
        StringAssert.Contains(receipt, "PR161 does not implement accepted approval records, policy satisfaction records, dry-run execution, patch artifacts, source apply, rollback, workflow continuation, or release readiness.");
        StringAssert.Contains(receipt, "PR161 names the L4 ladder. It does not climb it.");
    }

    private static string ReceiptPath() =>
        Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR161_L4_CAPABILITY_MATRIX.md");

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
