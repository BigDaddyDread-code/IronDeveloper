using System.Text.Json;

namespace IronDev.IntegrationTests;

[TestClass]
[TestCategory("WorkbenchBuilderPromptPreparation")]
public sealed class WorkbenchBuilderPromptPreparationSchemaContractTests
{
    private const string MigrationPath =
        "Database/migrate_workbench_builder_prompt_preparation.sql";

    [TestMethod]
    public void PreparedRun_FreezesExactRoleSpecificInputAndEveryHash()
    {
        var sql = Read(MigrationPath);
        foreach (var required in new[]
                 {
                     "BuilderExecutionAuthorizationId UNIQUEIDENTIFIER NOT NULL",
                     "BuilderWorkPackageCoreId UNIQUEIDENTIFIER NOT NULL",
                     "BuilderWorkPackageCoreSha256 CHAR(64) NOT NULL",
                     "BuilderAgentVersion NVARCHAR(100) NOT NULL",
                     "PromptVersion NVARCHAR(100) NOT NULL",
                     "ToolPolicyVersion NVARCHAR(100) NOT NULL",
                     "ContextSchemaVersion NVARCHAR(100) NOT NULL",
                     "OutputSchemaVersion NVARCHAR(100) NOT NULL",
                     "EffectiveProfileJson NVARCHAR(MAX) NOT NULL",
                     "EffectiveProfileSha256 CHAR(64) NOT NULL",
                     "SystemPrompt NVARCHAR(MAX) NOT NULL",
                     "PromptSha256 CHAR(64) NOT NULL",
                     "RoleContextJson NVARCHAR(MAX) NOT NULL",
                     "RoleContextSha256 CHAR(64) NOT NULL",
                     "ToolManifestJson NVARCHAR(MAX) NOT NULL",
                     "ToolManifestSha256 CHAR(64) NOT NULL",
                     "ProviderInputJson NVARCHAR(MAX) NOT NULL",
                     "ProviderInputSha256 CHAR(64) NOT NULL"
                 })
            StringAssert.Contains(sql, required);
    }

    [TestMethod]
    public void Preparation_IsSingleUseAtomicAndProviderFree()
    {
        var sql = Read(MigrationPath);
        var service = Read(
            "IronDev.Infrastructure/Services/WorkbenchBuilderPromptPreparationService.cs");

        StringAssert.Contains(sql, "UQ_BuilderAgentRuns_Authorization");
        StringAssert.Contains(sql, "TR_BuilderAgentRuns_ValidatePreparation");
        StringAssert.Contains(sql, "TR_BuilderAgentRuns_PreparationImmutable");
        StringAssert.Contains(sql, "CK_BuilderAgentRuns_NoInvocation");
        StringAssert.Contains(service, "IsolationLevel.Serializable");
        StringAssert.Contains(service, "ConsumedByBuilderExecutionRunId=@BuilderAgentRunId");
        StringAssert.Contains(service, "ProviderInvocationPermittedAtUtc = preparedAt");

        foreach (var forbidden in new[]
                 {
                     "IWorkbenchBusinessAnalystModelGateway",
                     "ILlmService",
                     "OpenAIClient",
                     "ChatClient",
                     "CompleteChat",
                     "InvokeProviderAsync"
                 })
            Assert.IsFalse(service.Contains(forbidden, StringComparison.Ordinal));
    }

    [TestMethod]
    public void DatabaseRevalidatesAuthorizationReadinessBaselineTicketsAndContextBeforeConsumption()
    {
        var sql = Read(MigrationPath);
        foreach (var required in new[]
                 {
                     "authz.ConsumedAtUtc IS NOT NULL",
                     "authz.RevokedAtUtc IS NOT NULL",
                     "authz.ExpiresAtUtc<=run.PreparedAtUtc",
                     "readiness.ExecutionReadiness",
                     "$.readinessAssessment.technicalEvidenceId",
                     "binding.CurrentRevision<>repositoryContext.RepositoryBindingRevision",
                     "binding.BaselineCommit<>repositoryContext.BaselineCommit",
                     "run.ObservedHeadCommit<>repositoryContext.BaselineCommit",
                     "workItem.CurrentContractId",
                     "contract.ContractHash",
                     "understanding.Status<>N''Superseded''",
                     "FROM dbo.ProjectUnderstandings newer"
                 })
            StringAssert.Contains(sql, required);
    }

    [TestMethod]
    public void Migration_IsRegisteredAndAppliedByApiTests()
    {
        using var migrations = JsonDocument.Parse(Read("Database/migrations.json"));
        var migration = migrations.RootElement.GetProperty("migrations")
            .EnumerateArray()
            .Single(entry => entry.GetProperty("path").GetString() == MigrationPath);
        Assert.AreEqual(
            "2026-07-workbench-pr07b-builder-prompt-preparation",
            migration.GetProperty("id").GetString());

        using var inventory = JsonDocument.Parse(Read("Database/sql-inventory.json"));
        var inventoryEntry = inventory.RootElement.GetProperty("entries")
            .EnumerateArray()
            .Single(entry => entry.GetProperty("path").GetString() == MigrationPath);
        Assert.IsTrue(inventoryEntry.GetProperty("appliedByManifest").GetBoolean());
        Assert.IsTrue(inventoryEntry.GetProperty("verifiedByScript").GetBoolean());

        var apiBase = Read("IronDev.IntegrationTests.Api/ApiTestBase.cs");
        StringAssert.Contains(apiBase, "migrate_workbench_builder_prompt_preparation.sql");
        var verifier = Read("Database/verify-migrations.ps1");
        StringAssert.Contains(verifier, "Prepared Builder AgentRun table");
        StringAssert.Contains(verifier, "Builder AgentRun provider not invoked");
    }

    private static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory);
             current is not null;
             current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "IronDev.slnx")))
                return current.FullName;
        }
        throw new DirectoryNotFoundException("Could not locate the IronDev repository root.");
    }
}
