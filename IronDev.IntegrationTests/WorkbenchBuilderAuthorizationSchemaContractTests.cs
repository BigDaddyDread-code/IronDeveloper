using System.Text.Json;

namespace IronDev.IntegrationTests;

[TestClass]
[TestCategory("WorkbenchBuilderAuthorization")]
public sealed class WorkbenchBuilderAuthorizationSchemaContractTests
{
    private const string MigrationPath =
        "Database/migrate_workbench_builder_authorization.sql";

    [TestMethod]
    public void Migration_StoresAnAuthorizationFreeCanonicalCoreAndExactOrderedChildren()
    {
        var sql = Read(MigrationPath);
        var core = Between(
            sql,
            "CREATE TABLE dbo.BuilderWorkPackageCores",
            "IF OBJECT_ID(N'dbo.BuilderWorkPackageTickets'");

        StringAssert.Contains(core, "CanonicalizationVersion INT NOT NULL");
        StringAssert.Contains(core, "CanonicalJson NVARCHAR(MAX) NOT NULL");
        StringAssert.Contains(core, "CoreHash CHAR(64) NOT NULL");
        StringAssert.Contains(core, "CreatedAtUtc DATETIME2(7) NOT NULL");
        StringAssert.Contains(core, "HASHBYTES('SHA2_256'");
        StringAssert.Contains(core, "Latin1_General_100_BIN2_UTF8");
        Assert.IsFalse(core.Contains("Authorization", StringComparison.Ordinal));
        Assert.IsFalse(core.Contains("ActorUserId", StringComparison.Ordinal));
        Assert.IsFalse(core.Contains("WorkbenchSessionId", StringComparison.Ordinal));
        Assert.IsFalse(core.Contains("LeaseEpoch", StringComparison.Ordinal));

        StringAssert.Contains(sql, "CREATE TABLE dbo.BuilderWorkPackageTickets");
        StringAssert.Contains(sql, "CREATE TABLE dbo.BuilderWorkPackageArtifactReferences");
        StringAssert.Contains(sql, "UQ_BuilderWorkPackageTickets_Ordinal");
        StringAssert.Contains(sql, "UQ_BuilderWorkPackageArtifactReferences_Ordinal");
        StringAssert.Contains(sql, "FK_BuilderWorkPackageTickets_Ticket");
        StringAssert.Contains(sql, "FK_BuilderWorkPackageArtifactReferences_Understanding");
        StringAssert.Contains(sql, "$.tickets[");
        StringAssert.Contains(sql, "$.governingArtifacts[");
        StringAssert.Contains(sql, "$.tenantId");
        StringAssert.Contains(sql, "workItem.CurrentContractId");
        StringAssert.Contains(sql, "workItemContractSha256");
        StringAssert.Contains(sql, "contract.ContractHash");
    }

    [TestMethod]
    public void RepositoryContext_BindsTheHumanBranchAnchorToExactRepositoryEvidence()
    {
        var sql = Read(MigrationPath);
        var context = Between(
            sql,
            "CREATE TABLE dbo.BuilderWorkPackageRepositoryContexts",
            "EXEC(N'CREATE OR ALTER TRIGGER dbo.TR_BuilderWorkPackageRepositoryContexts_ValidateAuthority");

        foreach (var column in new[]
                 {
                     "RepositoryBindingId UNIQUEIDENTIFIER NOT NULL",
                     "RepositoryBindingRevision BIGINT NOT NULL",
                     "BranchName NVARCHAR(255) NOT NULL",
                     "BaselineCommit CHAR(40) NOT NULL",
                     "RepositoryStateObservationId UNIQUEIDENTIFIER NOT NULL",
                     "WorktreeFingerprint CHAR(64) NOT NULL",
                     "ProjectExecutionProfileId UNIQUEIDENTIFIER NOT NULL",
                     "ProjectExecutionProfileRevision BIGINT NOT NULL",
                     "CodeIndexSnapshotId UNIQUEIDENTIFIER NOT NULL",
                     "CodeIndexSnapshotRevision BIGINT NOT NULL",
                     "BuildCommandSha256 CHAR(64) NOT NULL",
                     "TestCommandSha256 CHAR(64) NOT NULL",
                     "SandboxPolicyVersion NVARCHAR(100) NOT NULL",
                     "ToolchainManifestId NVARCHAR(200) NOT NULL"
                 })
        {
            StringAssert.Contains(context, column);
        }

        StringAssert.Contains(sql, "FK_BuilderWorkPackageRepositoryContexts_BindingRevision");
        StringAssert.Contains(sql, "FK_BuilderWorkPackageRepositoryContexts_ProfileRevision");
        StringAssert.Contains(sql, "FK_BuilderWorkPackageRepositoryContexts_Observation");
        StringAssert.Contains(sql, "FK_BuilderWorkPackageRepositoryContexts_CodeIndex");
        StringAssert.Contains(sql, "readiness.ExecutionReadiness<>N''Ready''");
        StringAssert.Contains(sql, "observation.RepositoryFingerprintSha256<>value.WorktreeFingerprint");
        StringAssert.Contains(sql, "codeIndex.TechnicalValidationAttemptId<>attempt.Id");
        StringAssert.Contains(sql, "attempt.AttemptNumber<>value.CodeIndexSnapshotRevision");
        foreach (var exactInput in new[]
                 {
                     "$.readinessAssessment.id",
                     "$.readinessAssessment.evidenceSha256",
                     "$.repositoryObservation.evidenceSha256",
                     "$.codeIndex.indexedContentSha256",
                     "$.codeIndex.sources",
                     "$.restoreCommandSha256",
                     "$.effectiveProfile.builderConfigurationId",
                     "$.effectiveProfile.builderConfigurationSha256",
                     "$.sandbox.qualificationAttemptId",
                     "$.sandbox.evidenceManifestSha256",
                     "$.sandbox.policySha256",
                     "$.sandbox.qualifiedImageDigest",
                     "$.sandbox.toolchainManifestSha256"
                 })
            StringAssert.Contains(sql, exactInput);
    }

    [TestMethod]
    public void Authorization_IsExactHashFencedFifteenMinuteAndSingleUse()
    {
        var sql = Read(MigrationPath);
        var authorization = Between(
            sql,
            "CREATE TABLE dbo.BuilderExecutionAuthorizations",
            "EXEC(N'CREATE OR ALTER TRIGGER dbo.TR_BuilderExecutionAuthorizations_ValidateGrant");

        StringAssert.Contains(authorization, "BuilderWorkPackageCoreHash CHAR(64) NOT NULL");
        StringAssert.Contains(authorization, "SingleUse BIT NOT NULL");
        StringAssert.Contains(authorization, "ConsumedAtUtc DATETIME2(7) NULL");
        StringAssert.Contains(authorization, "ConsumedByBuilderExecutionRunId UNIQUEIDENTIFIER NULL");
        StringAssert.Contains(authorization, "RevokedAtUtc DATETIME2(7) NULL");
        StringAssert.Contains(authorization, "GrantedByWorkbenchSessionId BIGINT NOT NULL");
        StringAssert.Contains(authorization, "GrantedUnderLeaseEpoch BIGINT NOT NULL");
        StringAssert.Contains(authorization, "FK_BuilderExecutionAuthorizations_CoreHash");
        StringAssert.Contains(authorization, "FK_BuilderExecutionAuthorizations_GrantFence");
        StringAssert.Contains(authorization, "FK_BuilderExecutionAuthorizations_GrantOperation");
        StringAssert.Contains(authorization, "SingleUse=1");
        StringAssert.Contains(authorization, "ExpiresAtUtc=DATEADD(MINUTE, 15, GrantedAtUtc)");
        StringAssert.Contains(sql, "TR_BuilderExecutionAuthorizations_TerminalImmutable");

        Assert.IsFalse(sql.Contains("CREATE TABLE dbo.BuilderExecutionRuns", StringComparison.Ordinal));
        Assert.IsFalse(sql.Contains("CREATE TABLE dbo.BuilderExecutionEnvelopes", StringComparison.Ordinal));
        Assert.IsFalse(sql.Contains("CREATE TABLE dbo.BuilderExecutionAttempts", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Pr07A_HasNoBuilderRunPromptOrProviderInvocationPath()
    {
        var service = Read(
            "IronDev.Infrastructure/Services/WorkbenchBuilderAuthorizationService.cs");
        var controller = Read(
            "IronDev.Api/Controllers/WorkbenchBuilderController.cs");
        var combined = service + controller;

        foreach (var forbidden in new[]
                 {
                     "ILlmProvider",
                     "ILlmService",
                     "IProviderGateway",
                     "CreateAgentRun",
                     "INSERT dbo.WorkbenchAgentRuns",
                     "INSERT dbo.WorkbenchAgentRunAttempts",
                     "PromptBuilder",
                     "ExecuteBuilder",
                     "RunBuilder"
                 })
            Assert.IsFalse(
                combined.Contains(forbidden, StringComparison.Ordinal),
                $"PR-07A must not contain provider/run authority: {forbidden}");
    }

    [TestMethod]
    public void ClientOperationAndOutboxLinks_AreTypedAndProjectScoped()
    {
        var sql = Read(MigrationPath);

        StringAssert.Contains(sql, "ResultBuilderWorkPackageCoreId UNIQUEIDENTIFIER NULL");
        StringAssert.Contains(sql, "ResultBuilderExecutionAuthorizationId UNIQUEIDENTIFIER NULL");
        StringAssert.Contains(sql, "FK_ClientOperations_BuilderWorkPackageCore");
        StringAssert.Contains(sql, "FK_ClientOperations_BuilderExecutionAuthorization");
        StringAssert.Contains(sql, "FK_WorkbenchOutboxEvents_BuilderWorkPackageCore");
        StringAssert.Contains(sql, "FK_WorkbenchOutboxEvents_BuilderExecutionAuthorization");
        StringAssert.Contains(sql, "(TenantId, ResultProjectId, ResultBuilderWorkPackageCoreId)");
        StringAssert.Contains(sql, "(TenantId, ResultProjectId, ResultBuilderExecutionAuthorizationId)");
    }

    [TestMethod]
    public void Migration_IsRegisteredInTheManifestInventoryAndVerifier()
    {
        using var migrations = JsonDocument.Parse(Read("Database/migrations.json"));
        var migration = migrations.RootElement.GetProperty("migrations")
            .EnumerateArray()
            .Single(entry => entry.GetProperty("path").GetString() == MigrationPath);

        Assert.AreEqual(
            "2026-07-workbench-pr07a-builder-authorization",
            migration.GetProperty("id").GetString());
        var description = migration.GetProperty("description").GetString()!;
        StringAssert.Contains(description, "authorization-free canonical Builder work-package cores");
        StringAssert.Contains(description, "without creating Builder runs or execution envelopes");

        using var inventory = JsonDocument.Parse(Read("Database/sql-inventory.json"));
        var entry = inventory.RootElement.GetProperty("entries")
            .EnumerateArray()
            .Single(item => item.GetProperty("path").GetString() == MigrationPath);

        Assert.AreEqual(
            "database.migrate-workbench-builder-authorization",
            entry.GetProperty("id").GetString());
        Assert.IsTrue(entry.GetProperty("appliedByManifest").GetBoolean());
        Assert.IsTrue(entry.GetProperty("verifiedByScript").GetBoolean());

        var created = entry.GetProperty("objectsCreated")
            .EnumerateArray()
            .Select(item => item.GetString())
            .ToArray();
        CollectionAssert.Contains(created, "dbo.BuilderWorkPackageCores");
        CollectionAssert.Contains(created, "dbo.BuilderWorkPackageRepositoryContexts");
        CollectionAssert.Contains(created, "dbo.BuilderExecutionAuthorizations");

        var verifier = Read("Database/verify-migrations.ps1");
        StringAssert.Contains(verifier, "Builder authorization-free core table");
        StringAssert.Contains(verifier, "Builder branch-bound exact repository context");
        StringAssert.Contains(verifier, "Builder authorization exact grant fence and operation");
        StringAssert.Contains(verifier, "Builder ClientOperation result authority");
        StringAssert.Contains(verifier, "Builder typed outbox authority");
    }

    private static string Between(string value, string start, string end)
    {
        var startIndex = value.IndexOf(start, StringComparison.Ordinal);
        Assert.IsTrue(startIndex >= 0, $"Missing start marker: {start}");
        var endIndex = value.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.IsTrue(endIndex > startIndex, $"Missing end marker: {end}");
        return value[startIndex..endIndex];
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
