using IronDev.Core.KnowledgeCompiler;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("Governance")]
[TestCategory("Weaviate")]
[TestCategory("SemanticMemory")]
[TestCategory("ProjectionRebuild")]
[TestCategory("Boundary")]
[TestCategory("Contract")]
public sealed class WeaviateRebuildCommandHardeningTests
{
    private const string ReceiptRelativePath = "Docs/receipts/H13_WEAVIATE_REBUILD_COMMAND_HARDENING.md";
    private const string WeaviateServiceRelativePath = "IronDev.Infrastructure/Services/SemanticMemory/WeaviateSemanticMemoryService.cs";
    private const string InMemoryServiceRelativePath = "IronDev.Infrastructure/Services/SemanticMemory/InMemorySemanticMemoryService.cs";
    private const string ModelsRelativePath = "IronDev.Core/KnowledgeCompiler/SemanticIndexRebuildModels.cs";
    private const string InterfaceRelativePath = "IronDev.Core/KnowledgeCompiler/ISemanticMemoryService.cs";

    [TestMethod]
    public void WeaviateRebuild_DefaultProjectRebuildDoesNotDeleteSharedCollection()
    {
        var plan = SemanticIndexRebuildGuard.BuildPlan(
            new SemanticIndexRebuildRequest { ProjectId = 42 },
            collectionName: "IronDevKnowledge",
            weaviateEnabled: true,
            sourceDocumentCount: 3,
            estimatedChunkCount: 6);

        Assert.AreEqual(42, plan.ProjectId);
        Assert.AreEqual(SemanticIndexRebuildMode.ProjectOnly, plan.Mode);
        Assert.IsFalse(plan.IsDestructive);
        Assert.IsFalse(plan.WillDeleteCollection);
        Assert.IsFalse(plan.WillMutateSqlSourceRecords);
        Assert.IsFalse(plan.WillMutateAuthorityRecords);
        Assert.AreEqual(0, plan.BlockReasons.Count);

        var rebuildMethod = ExtractWeaviateResultRebuildMethod();
        AssertDoesNotContain(rebuildMethod, "Collections.Delete(");
        AssertDoesNotContain(rebuildMethod, ".Collections.Delete");
    }

    [TestMethod]
    public void WeaviateRebuild_MissingProjectIdFailsClosed()
    {
        var plan = SemanticIndexRebuildGuard.BuildPlan(
            new SemanticIndexRebuildRequest { ProjectId = 0 },
            collectionName: "IronDevKnowledge",
            weaviateEnabled: true);

        CollectionAssert.Contains(plan.BlockReasons.ToList(), SemanticIndexRebuildBlockReason.MissingProjectId);

        var result = SemanticIndexRebuildGuard.Blocked(plan, DateTime.UtcNow);
        Assert.AreEqual(SemanticIndexRebuildStatus.Blocked, result.Status);
        Assert.AreEqual(SemanticIndexRebuildBlockReason.MissingProjectId, result.FailureReason);
        Assert.IsFalse(result.IsAuthorityGrant);
    }

    [TestMethod]
    public void WeaviateRebuild_SharedCollectionResetIsBlockedByDefault()
    {
        var resetByFlag = SemanticIndexRebuildGuard.BuildPlan(
            new SemanticIndexRebuildRequest
            {
                ProjectId = 42,
                AllowCollectionReset = true
            },
            collectionName: "IronDevKnowledge",
            weaviateEnabled: true);

        var resetByMode = SemanticIndexRebuildGuard.BuildPlan(
            new SemanticIndexRebuildRequest
            {
                ProjectId = 42,
                Mode = SemanticIndexRebuildMode.FullCollectionResetBlocked
            },
            collectionName: "IronDevKnowledge",
            weaviateEnabled: true);

        CollectionAssert.Contains(resetByFlag.BlockReasons.ToList(), SemanticIndexRebuildBlockReason.UnsafeSharedCollectionReset);
        CollectionAssert.Contains(resetByMode.BlockReasons.ToList(), SemanticIndexRebuildBlockReason.UnsafeSharedCollectionReset);
        Assert.IsFalse(resetByFlag.WillDeleteCollection);
        Assert.IsFalse(resetByMode.WillDeleteCollection);
    }

    [TestMethod]
    public void WeaviateRebuild_DisabledOrUnavailableIsExplainable()
    {
        var disabledPlan = SemanticIndexRebuildGuard.BuildPlan(
            new SemanticIndexRebuildRequest { ProjectId = 42 },
            collectionName: "IronDevKnowledge",
            weaviateEnabled: false);

        CollectionAssert.Contains(disabledPlan.BlockReasons.ToList(), SemanticIndexRebuildBlockReason.WeaviateDisabled);

        var disabledResult = SemanticIndexRebuildGuard.Blocked(disabledPlan, DateTime.UtcNow);
        Assert.AreEqual(SemanticIndexRebuildStatus.Blocked, disabledResult.Status);
        Assert.AreEqual(SemanticIndexRebuildBlockReason.WeaviateDisabled, disabledResult.FailureReason);

        var failedResult = SemanticIndexRebuildGuard.Failed(
            disabledPlan,
            DateTime.UtcNow,
            runId: string.Empty,
            processedDocuments: 0,
            SemanticIndexRebuildBlockReason.WeaviateUnavailable,
            "Failed to initialize Weaviate client. Check endpoint and authentication configuration.");

        Assert.AreEqual(SemanticIndexRebuildStatus.Failed, failedResult.Status);
        Assert.AreEqual(SemanticIndexRebuildBlockReason.WeaviateUnavailable, failedResult.FailureReason);
        StringAssert.Contains(failedResult.ErrorMessage, "Weaviate");
    }

    [TestMethod]
    public void WeaviateRebuild_ResultDoesNotGrantAuthority()
    {
        var plan = SemanticIndexRebuildGuard.BuildPlan(
            new SemanticIndexRebuildRequest { ProjectId = 42 },
            collectionName: "IronDevKnowledge",
            weaviateEnabled: true,
            sourceDocumentCount: 2,
            estimatedChunkCount: 4);

        var result = SemanticIndexRebuildGuard.Completed(
            plan,
            DateTime.UtcNow,
            runId: "semantic-index-run:42",
            processedDocuments: 2);

        Assert.IsFalse(result.IsAuthorityGrant);
        Assert.IsFalse(result.GrantsApproval);
        Assert.IsFalse(result.GrantsPolicySatisfaction);
        Assert.IsFalse(result.GrantsSourceApplyAuthority);
        Assert.IsFalse(result.GrantsWorkflowContinuation);
        Assert.IsFalse(result.GrantsReleaseReadiness);
        Assert.IsFalse(result.GrantsDeploymentReadiness);

        var warnings = string.Join("\n", result.Warnings);
        AssertContainsAll(
            warnings,
            "Weaviate rebuild restores recall. It does not restore authority.",
            "SQL remains source of truth.",
            "Weaviate remains a rebuildable derived index.",
            "A rebuilt vector index is still just an index.",
            "Vector recall is not authority.");
    }

    [TestMethod]
    public void WeaviateRebuild_DoesNotMutateSourceOrAuthorityRecords()
    {
        var plan = SemanticIndexRebuildGuard.BuildPlan(
            new SemanticIndexRebuildRequest { ProjectId = 42 },
            collectionName: "IronDevKnowledge",
            weaviateEnabled: true);

        Assert.IsFalse(plan.WillMutateSqlSourceRecords);
        Assert.IsFalse(plan.WillMutateAuthorityRecords);

        var source = WeaviateServiceSource();
        AssertDoesNotContain(source, "AcceptedApproval");
        AssertDoesNotContain(source, "PolicySatisfaction");
        AssertDoesNotContain(source, "SourceApplyReceipt");
        AssertDoesNotContain(source, "RollbackExecutionReceipt");
        AssertDoesNotContain(source, "WorkflowTransition");
        AssertDoesNotContain(source, "ReleaseReadinessDecision");
    }

    [TestMethod]
    public void WeaviateRebuild_PreservesH10H11PayloadBoundaries()
    {
        var receipt = ReceiptText();
        var models = ReadRepositoryFile(ModelsRelativePath);

        foreach (var text in new[] { receipt, models })
        {
            AssertContainsAll(
                text,
                "raw secrets",
                "raw artifact bodies",
                "raw private payloads",
                "Vector indexing is not redaction.");
        }

        AssertContainsAll(
            receipt,
            "Weaviate/vector text must come from safe summaries or approved redacted content where applicable.",
            "H13 does not implement raw payload redaction.",
            "H13 does not implement artifact retention.");
    }

    [TestMethod]
    public void WeaviateRebuild_DoesNotIntroduceApiCliUiOrConfigChanges()
    {
        var root = RepositoryRoot();
        var receipt = ReceiptText();

        Assert.IsFalse(File.Exists(Path.Combine(root, "Database", "migrate_h13_weaviate_rebuild_command_hardening.sql")), "H13 must not add a SQL migration.");
        AssertFileDoesNotContain("Database/migrations.json", "h13");
        AssertFileDoesNotContain("Database/apply-migrations.ps1", "h13");
        AssertFileDoesNotContain("Database/verify-migrations.ps1", "h13");
        AssertFileDoesNotContain("Scripts/weaviate-dev.ps1", "h13");
        AssertFileDoesNotContain("docker-compose.yml", "h13");
        AssertFileDoesNotContain("docker-compose.override.yml", "h13");

        foreach (var relativeDirectory in new[]
        {
            "Database",
            "IronDev.Api",
            "tools/IronDev.Cli",
            "IronDev.TauriShell",
            ".github/workflows"
        })
        {
            var directory = Path.Combine(root, relativeDirectory.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(directory))
                continue;

            var h13Files = Directory
                .EnumerateFiles(directory, "*h13*", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(directory, "*H13*", SearchOption.AllDirectories))
                .Select(path => Path.GetRelativePath(root, path))
                .ToArray();

            Assert.AreEqual(0, h13Files.Length, $"H13 implementation file appeared in forbidden path {relativeDirectory}: {string.Join(", ", h13Files)}");
        }

        AssertContainsAll(
            receipt,
            "H13 does not add a SQL migration.",
            "H13 does not alter tables.",
            "H13 does not add indexes.",
            "H13 does not alter stored procedures.",
            "H13 does not change API/CLI/UI behavior.",
            "H13 does not change Docker compose behavior.",
            "H13 does not change Weaviate auth/prod config.");
    }

    [TestMethod]
    public void Receipt_RecordsRebuildScopeAndLimitations()
    {
        var receipt = ReceiptText();

        AssertContainsAll(
            receipt,
            "H13 hardens Weaviate rebuild command behavior.",
            "Hardening outcome selected: `SafeHardeningImplemented`.",
            "Project-scoped rebuild must not silently delete/reset a shared collection.",
            "H13 does not make Weaviate authoritative.",
            "H13 does not restore authority.",
            "H13 does not recreate authority records.",
            "H13 does not grant approval.",
            "H13 does not grant policy satisfaction.",
            "H13 does not grant source-apply authority.",
            "H13 does not grant workflow continuation authority.",
            "H13 does not grant release readiness.",
            "H13 does not grant deployment readiness.",
            "SQL remains source of truth.",
            "Weaviate remains a rebuildable derived index.",
            "A rebuilt vector index is still just an index.",
            "H14 owns Weaviate auth/prod config tests.");

        AssertContainsAll(
            ReadRepositoryFile(InterfaceRelativePath),
            "SemanticIndexRebuildRequest",
            "SemanticIndexRebuildResult");
        AssertContainsAll(
            ReadRepositoryFile(InMemoryServiceRelativePath),
            "SemanticIndexRebuildRequest",
            "SemanticIndexRebuildResult");
    }

    private static string ExtractWeaviateResultRebuildMethod()
    {
        var source = WeaviateServiceSource();
        const string startMarker = "public async Task<SemanticIndexRebuildResult> RebuildIndexAsync(";
        const string endMarker = "public async Task RebuildProjectAsync";
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.IsTrue(start >= 0, "Could not find result-returning Weaviate rebuild method.");
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.IsTrue(end > start, "Could not find end of result-returning Weaviate rebuild method.");
        return source[start..end];
    }

    private static void AssertFileDoesNotContain(string relativePath, string marker)
    {
        var absolutePath = Path.Combine(RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(absolutePath))
            return;

        var text = File.ReadAllText(absolutePath);
        AssertDoesNotContain(text, marker);
    }

    private static void AssertContainsAll(string text, params string[] expected)
    {
        foreach (var value in expected)
            StringAssert.Contains(text, value);
    }

    private static void AssertDoesNotContain(string text, string value)
    {
        Assert.IsFalse(text.Contains(value, StringComparison.OrdinalIgnoreCase), $"Did not expect to find '{value}'.");
    }

    private static string WeaviateServiceSource() => ReadRepositoryFile(WeaviateServiceRelativePath);

    private static string ReceiptText() => ReadRepositoryFile(ReceiptRelativePath);

    private static string ReadRepositoryFile(string relativePath) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
