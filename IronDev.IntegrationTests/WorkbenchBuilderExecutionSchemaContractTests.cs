using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class WorkbenchBuilderExecutionSchemaContractTests
{
    [TestMethod]
    public void Pr07C_HasSingleClaimBoundedAttemptsAndImmutableEvidence()
    {
        var sql = Read("Database/migrate_workbench_builder_execution.sql");
        StringAssert.Contains(sql, "CK_BuilderAgentRuns_ExecutionState");
        StringAssert.Contains(sql, "ExecutionClaimId");
        StringAssert.Contains(sql, "AttemptNumber BETWEEN 1 AND 3");
        StringAssert.Contains(sql, "TR_BuilderAgentRunAttempts_Immutable");
        StringAssert.Contains(sql, "TR_BuilderAgentRunProposedFiles_Immutable");
        StringAssert.Contains(sql, "TR_BuilderAgentRunToolCalls_Immutable");
        StringAssert.Contains(sql, "RawPatchSha256");
        StringAssert.Contains(sql, "ChangedFileManifestSha256");
        StringAssert.Contains(sql, "SandboxEvidenceManifestSha256");
    }

    [TestMethod]
    public void Pr07C_UsesRoleSpecificGatewayAndQualifiedSandboxWithoutApplyAuthority()
    {
        var service = Read("IronDev.Infrastructure/Services/WorkbenchBuilderExecutionService.cs");
        var gateway = Read("IronDev.Infrastructure/Services/WorkbenchBuilderModelGateway.cs");
        var runner = Read("IronDev.Infrastructure/Services/Sandbox/WorkbenchBuilderSandboxRunner.cs");
        StringAssert.Contains(service, "IWorkbenchBuilderModelGateway");
        StringAssert.Contains(service, "IWorkbenchBuilderSandboxRunner");
        StringAssert.Contains(service, "BuilderOutputValidator.Validate");
        StringAssert.Contains(gateway, "SkeletonAgentRole.Builder");
        StringAssert.Contains(runner, "ISandboxExecutionService");
        StringAssert.Contains(runner, "SandboxSourceSnapshotRequest");
        Assert.IsFalse(service.Contains("ControlledWorktreeApply", StringComparison.Ordinal));
        Assert.IsFalse(service.Contains("UPDATE dbo.ProjectTickets", StringComparison.Ordinal));
        Assert.IsFalse(service.Contains("UPDATE dbo.WorkItemContracts", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Pr07C_IsRegisteredAfterPreparationMigration()
    {
        var manifest = Read("Database/migrations.json");
        var preparation = manifest.IndexOf("pr07b-builder-prompt-preparation", StringComparison.Ordinal);
        var execution = manifest.IndexOf("pr07c-builder-execution", StringComparison.Ordinal);
        Assert.IsTrue(preparation >= 0 && execution > preparation);
        StringAssert.Contains(Read("Database/verify-migrations.ps1"), "Builder bounded attempts");
        StringAssert.Contains(Read("IronDev.IntegrationTests.Api/ApiTestBase.cs"),
            "migrate_workbench_builder_execution.sql");
    }

    private static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
