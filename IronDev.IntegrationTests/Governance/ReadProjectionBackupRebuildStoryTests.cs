using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("Governance")]
[TestCategory("ReadProjection")]
[TestCategory("ProjectionRebuild")]
[TestCategory("Backup")]
[TestCategory("Policy")]
[TestCategory("Boundary")]
[TestCategory("Contract")]
public sealed class ReadProjectionBackupRebuildStoryTests
{
    private const string StoryRelativePath = "Docs/architecture/H12_READ_PROJECTION_BACKUP_REBUILD_STORY.md";
    private const string ReceiptRelativePath = "Docs/receipts/H12_READ_PROJECTION_BACKUP_REBUILD_STORY.md";

    private static readonly string[] RequiredNonRebuildableAuthorityRecords =
    [
        "governance events",
        "accepted approvals",
        "policy satisfaction records",
        "tool requests",
        "tool gate decisions",
        "approval decisions",
        "controlled dry-run receipts",
        "patch artifacts",
        "rollback support receipts",
        "source-apply dry-run receipts",
        "source-apply receipts",
        "rollback execution receipts",
        "workflow transition records",
        "release readiness decision records",
        "durable memory proposal decisions"
    ];

    private static readonly string[] RequiredDerivedSurfaces =
    [
        "operation status summaries/projections",
        "operation timeline read models",
        "frontend readiness read models",
        "evidence metadata read models",
        "receipt metadata read models",
        "validation-result metadata read models",
        "patch-package metadata read models",
        "interrupted-run read models",
        "rollback-recovery read models",
        "worktree/base/head freshness read models",
        "status/error envelope read models",
        "read-side cache/index state",
        "Weaviate/vector indexes",
        "unknown read surface",
        "authority/source records"
    ];

    private static readonly string[] RequiredFailureClasses =
    [
        "SourceRecordsMissing",
        "SourceRecordsCorrupt",
        "SchemaVersionUnsupported",
        "ProjectionOrderingAmbiguous",
        "TenantScopeMismatch",
        "ProjectionWriteFailed",
        "VerificationFailed",
        "PartialRebuildDetected",
        "VectorIndexRebuildFailed",
        "ManualReviewRequired"
    ];

    [TestMethod]
    public void ReadProjectionRebuildStory_DefinesSourceOfTruthChain()
    {
        var story = StoryText();
        var receipt = ReceiptText();

        foreach (var text in new[] { story, receipt })
        {
            AssertContainsAll(
                text,
                "SQL is source of truth.",
                "Authority records are durable source records.",
                "Read models may be rebuildable.",
                "Weaviate/vector indexes are rebuildable derived indexes.",
                "Durable source records -> deterministic projection/rebuild process -> read model / index / cache -> display/query surface",
                "The chain does not work in reverse.",
                "A rebuilt projection is not the source of truth.");
        }
    }

    [TestMethod]
    public void ReadProjectionRebuildStory_ClassifiesAuthorityRecordsAsNonRebuildable()
    {
        var story = StoryText();
        var receipt = ReceiptText();

        foreach (var authorityRecord in RequiredNonRebuildableAuthorityRecords)
        {
            StringAssert.Contains(story, authorityRecord);
            StringAssert.Contains(receipt, authorityRecord);
        }

        foreach (var text in new[] { story, receipt })
        {
            AssertContainsAll(
                text,
                "These records must be backed up and preserved.",
                "They must not be reconstructed from projections, vector indexes, UI state, receipt summaries, safe summaries, or read models.",
                "`NotRebuildableAuthorityRecord`");
        }
    }

    [TestMethod]
    public void ReadProjectionRebuildStory_ClassifiesDerivedSurfacesAsRebuildable()
    {
        var story = StoryText();
        var receipt = ReceiptText();

        foreach (var surface in RequiredDerivedSurfaces)
        {
            StringAssert.Contains(story, surface);
            StringAssert.Contains(receipt, surface);
        }

        AssertContainsAll(
            story,
            "`RebuildableProjection`",
            "`RebuildableCache`",
            "`RebuildableIndex`",
            "`ManualReviewRequired`",
            "`UnknownRequiresReview`",
            "| Weaviate/vector indexes | `RebuildableIndex` |");
    }

    [TestMethod]
    public void ReadProjectionRebuildStory_DefinesFutureRebuildRequirements()
    {
        var story = StoryText();
        var receipt = ReceiptText();

        foreach (var text in new[] { story, receipt })
        {
            AssertContainsAll(
                text,
                "explicit target projection",
                "explicit source record set",
                "explicit tenant/project scope",
                "source schema/version compatibility check",
                "deterministic ordering rule",
                "checkpoint/cursor handling",
                "idempotency rule",
                "stale projection clearing rule",
                "dry-run mode",
                "verification step",
                "receipt/evidence of rebuild attempt",
                "failure classification",
                "no authority mutation",
                "no source-record mutation",
                "no approval/policy/source-apply/release/deploy grant");
        }
    }

    [TestMethod]
    public void ReadProjectionRebuildStory_DefinesTenantAndFailureBoundaries()
    {
        var story = StoryText();
        var receipt = ReceiptText();

        foreach (var failureClass in RequiredFailureClasses)
        {
            StringAssert.Contains(story, $"`{failureClass}`");
            StringAssert.Contains(receipt, $"`{failureClass}`");
        }

        foreach (var text in new[] { story, receipt })
        {
            AssertContainsAll(
                text,
                "rebuild all tenants by default",
                "mix tenant data",
                "rebuild Tenant A projection from Tenant B source records",
                "allow missing TenantId to mean all tenants",
                "rely on project-only scope where TenantId is required",
                "Tenant-scoped rebuild is still not authority.",
                "Failures must become explainable states, not silent partial reads.");
        }
    }

    [TestMethod]
    public void ReadProjectionRebuildStory_DefersWeaviateCommandHardeningToH13()
    {
        var story = StoryText();
        var receipt = ReceiptText();

        foreach (var text in new[] { story, receipt })
        {
            AssertContainsAll(
                text,
                "H13 owns Weaviate rebuild command hardening.",
                "vector indexes are rebuildable derived indexes",
                "vector recall is not authority",
                "vector content must come from safe summaries or approved redacted content",
                "deleting/rebuilding vector indexes does not delete or recreate source records",
                "vector rebuild failure must not block authority records from existing",
                "vector rebuild success does not approve anything",
                "H12 does not implement Weaviate behavior.");
        }
    }

    [TestMethod]
    public void ReadProjectionRebuildStory_DoesNotTreatRebuildAsAuthority()
    {
        var story = StoryText();
        var receipt = ReceiptText();

        foreach (var text in new[] { story, receipt })
        {
            AssertContainsAll(
                text,
                "Projection rebuild plans restore read models. They do not recreate authority records.",
                "Backup is not authority.",
                "Rebuild is not authority.",
                "A rebuilt projection is not approval.",
                "A rebuilt projection is not policy satisfaction.",
                "A rebuilt projection is not source-apply authority.",
                "A rebuilt projection is not workflow continuation authority.",
                "A rebuilt projection is not merge readiness.",
                "A rebuilt projection is not release readiness.",
                "A rebuilt projection is not deployment readiness.",
                "A rebuilt projection is not rollback authority.",
                "A rebuilt projection is not retry authority.",
                "A rebuilt projection is not mutation authority.",
                "A rebuilt projection does not prove source records are true.",
                "A rebuilt projection does not recreate missing authority records.",
                "A rebuilt projection is not the source of truth.");
        }
    }

    [TestMethod]
    public void ReadProjectionRebuildStory_DoesNotIntroduceImplementationOrStorageChanges()
    {
        var root = RepositoryRoot();
        var receipt = ReceiptText();

        Assert.IsFalse(File.Exists(Path.Combine(root, "Database", "migrate_h12_read_projection_backup_rebuild.sql")), "H12 must not add a SQL migration.");
        AssertFileDoesNotContain("Database/migrations.json", "h12");
        AssertFileDoesNotContain("Database/apply-migrations.ps1", "h12");
        AssertFileDoesNotContain("Database/verify-migrations.ps1", "h12");
        AssertFileDoesNotContain("Docs/testing/SLOW_TEST_QUARANTINE_REGISTER.md", "H12_READ_PROJECTION");

        foreach (var relativeDirectory in new[]
        {
            "Database",
            "IronDev.Api",
            "IronDev.Core",
            "IronDev.Infrastructure",
            "IronDev.TauriShell",
            "tools",
            ".github/workflows"
        })
        {
            var directory = Path.Combine(root, relativeDirectory.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(directory))
                continue;

            var h12Files = Directory
                .EnumerateFiles(directory, "*h12*", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(directory, "*H12*", SearchOption.AllDirectories))
                .Select(path => Path.GetRelativePath(root, path))
                .ToArray();

            Assert.AreEqual(0, h12Files.Length, $"H12 implementation file appeared in forbidden path {relativeDirectory}: {string.Join(", ", h12Files)}");
        }

        AssertContainsAll(
            receipt,
            "H12 defines story/policy only.",
            "H12 does not implement backup jobs.",
            "H12 does not implement rebuild commands.",
            "H12 does not implement projection replay.",
            "H12 does not add a SQL migration.",
            "H12 does not alter tables.",
            "H12 does not add indexes.",
            "H12 does not alter stored procedures.",
            "H12 does not alter triggers.",
            "H12 does not change permissions.",
            "H12 does not change API/CLI/UI behavior.",
            "H12 does not change Weaviate behavior.",
            "H12 does not change workflow/source-apply/rollback/release/deployment authority.");
    }

    [TestMethod]
    public void Receipt_RecordsStoryScopeAndLimitations()
    {
        var receipt = ReceiptText();

        AssertContainsAll(
            receipt,
            "H12 defines story/policy only.",
            "H12 does not implement backup.",
            "H12 does not implement rebuild.",
            "H12 does not verify existing backups.",
            "H12 does not verify existing projections are rebuildable.",
            "H12 does not prove existing source records are complete.",
            "H12 does not prove historical projection rows are correct.",
            "H12 does not define exact backup retention windows.",
            "H12 does not implement H13.",
            "H13 - Weaviate rebuild command hardening.");
    }

    private static void AssertFileDoesNotContain(string relativePath, string marker)
    {
        var absolutePath = Path.Combine(RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(absolutePath))
            return;

        var text = File.ReadAllText(absolutePath);
        Assert.IsFalse(text.Contains(marker, StringComparison.OrdinalIgnoreCase), $"{relativePath} must not contain '{marker}'.");
    }

    private static void AssertContainsAll(string text, params string[] expected)
    {
        foreach (var value in expected)
            StringAssert.Contains(text, value);
    }

    private static string StoryText() =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), StoryRelativePath));

    private static string ReceiptText() =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), ReceiptRelativePath));

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
