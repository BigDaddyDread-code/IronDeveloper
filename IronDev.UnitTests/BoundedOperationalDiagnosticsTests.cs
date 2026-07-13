using IronDev.Core.Operations;

namespace IronDev.UnitTests;

[TestClass]
public sealed class BoundedOperationalDiagnosticsTests
{
    [TestMethod]
    public void Required_diagnostic_categories_are_complete_and_bounded()
    {
        CollectionAssert.AreEquivalent(
            new[]
            {
                BackendDependencyKind.ApiProcess,
                BackendDependencyKind.DatabaseConnection,
                BackendDependencyKind.ModelProvider,
                BackendDependencyKind.VectorProvider,
                BackendDependencyKind.WorkspaceRoot,
                BackendDependencyKind.GitExecutable,
                BackendDependencyKind.DiskCapacity,
                BackendDependencyKind.MigrationState,
                BackendDependencyKind.BackgroundReindexState
            },
            BackendOperationalHealthBoundaries.RequiredOperationalDiagnostics.ToArray());
    }

    [TestMethod]
    public void Diagnostic_boundaries_remain_read_only_and_non_authoritative()
    {
        var warnings = string.Join(" ", BackendOperationalHealthBoundaries.Warnings);
        StringAssert.Contains(warnings, "read-only");
        StringAssert.Contains(warnings, "not authority");
        StringAssert.Contains(warnings, "not migration execution");
    }

    [TestMethod]
    public void Configuration_declared_runtime_state_is_never_promoted_to_available_evidence()
    {
        Assert.AreEqual(
            BackendDependencyHealthStatus.NotConfigured,
            BackendOperationalHealthBoundaries.ClassifyUnverifiedDeclaredState(null));
        Assert.AreEqual(
            BackendDependencyHealthStatus.Degraded,
            BackendOperationalHealthBoundaries.ClassifyUnverifiedDeclaredState("Current"));
        Assert.AreNotEqual(
            BackendDependencyHealthStatus.Available,
            BackendOperationalHealthBoundaries.ClassifyUnverifiedDeclaredState("Completed"));
    }
}
