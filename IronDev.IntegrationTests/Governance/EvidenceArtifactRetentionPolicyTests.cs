using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("Governance")]
[TestCategory("ArtifactRetention")]
[TestCategory("EvidenceArtifact")]
[TestCategory("Retention")]
[TestCategory("Policy")]
[TestCategory("Boundary")]
[TestCategory("Contract")]
public sealed class EvidenceArtifactRetentionPolicyTests
{
    private const string PolicyRelativePath = "Docs/policies/H11_EVIDENCE_ARTIFACT_RETENTION_POLICY.md";
    private const string ReceiptRelativePath = "Docs/receipts/H11_EVIDENCE_ARTIFACT_RETENTION_POLICY.md";

    private static readonly string[] RequiredArtifactClasses =
    [
        "ReferenceOnlyArtifact",
        "ShortLivedDiagnosticArtifact",
        "GovernedEvidenceArtifact",
        "RecoveryCriticalArtifact",
        "RedactedArtifact",
        "StoreProhibitedArtifact",
        "ExternalArtifactReference"
    ];

    private static readonly string[] RequiredLifecycleStates =
    [
        "Captured",
        "Classified",
        "Redacted",
        "Retained",
        "Held",
        "DeletionEligible",
        "Deleted",
        "ExternalOnly",
        "Unknown"
    ];

    private static readonly string[] RequiredMetadataTerms =
    [
        "artifact ID",
        "artifact type",
        "retention class",
        "artifact reference",
        "artifact hash",
        "artifact owner",
        "project/tenant scope",
        "source operation ID",
        "source receipt/governance event",
        "captured timestamp",
        "retention reason",
        "redaction status",
        "access boundary",
        "expiry/review timestamp",
        "legal/audit/recovery hold status",
        "external storage indicator",
        "deletion eligibility state"
    ];

    [TestMethod]
    public void EvidenceArtifactPolicy_DefinesArtifactClasses()
    {
        var policy = PolicyText();
        var receipt = ReceiptText();

        foreach (var artifactClass in RequiredArtifactClasses)
        {
            StringAssert.Contains(policy, $"`{artifactClass}`");
            StringAssert.Contains(receipt, $"`{artifactClass}`");
        }

        AssertContainsAll(
            policy,
            "## 3. Artifact Retention Classes",
            "| `ReferenceOnlyArtifact` | Retain reference metadata | No raw body |",
            "| `ExternalArtifactReference` | Record reference only | External retention remains outside IronDev unless managed |",
            "External retention is not controlled unless an integration explicitly manages it.");
    }

    [TestMethod]
    public void EvidenceArtifactPolicy_DefinesLifecycleStates()
    {
        var policy = PolicyText();
        var receipt = ReceiptText();

        foreach (var lifecycleState in RequiredLifecycleStates)
        {
            StringAssert.Contains(policy, $"`{lifecycleState}`");
            StringAssert.Contains(receipt, $"`{lifecycleState}`");
        }

        AssertContainsAll(
            policy,
            "These are policy vocabulary only unless already implemented elsewhere.",
            "H11 does not add lifecycle state columns.",
            "H11 does not add lifecycle state transitions.",
            "H11 does not add an artifact lifecycle executor.");
    }

    [TestMethod]
    public void EvidenceArtifactPolicy_DefinesFutureMetadataRequirements()
    {
        var policy = PolicyText();
        var receipt = ReceiptText();

        foreach (var metadataTerm in RequiredMetadataTerms)
        {
            StringAssert.Contains(policy, metadataTerm);
            StringAssert.Contains(receipt, metadataTerm);
        }

        AssertContainsAll(
            policy,
            "Future artifact retention implementation must require:",
            "H11 does not add this metadata to storage. H11 defines future requirements.");
    }

    [TestMethod]
    public void EvidenceArtifactPolicy_DefinesDeletionPrerequisitesWithoutImplementingDeletion()
    {
        var policy = PolicyText();
        var receipt = ReceiptText();

        foreach (var text in new[] { policy, receipt })
        {
            AssertContainsAll(
                text,
                "artifact has a known retention class",
                "artifact is not `StoreProhibitedArtifact`",
                "artifact is not under legal/audit/recovery hold",
                "artifact no longer supports active rollback/recovery/review",
                "artifact expiry/review date has passed where applicable",
                "deletion scope is known",
                "downstream copies are understood or explicitly out of scope",
                "deletion is logged as evidence of lifecycle action",
                "H11 does not implement deletion.",
                "H11 does not implement artifact expiry jobs.",
                "H11 does not implement retention deletion jobs.",
                "H11 does not implement cleanup commands.");
        }
    }

    [TestMethod]
    public void EvidenceArtifactPolicy_DefinesExternalAndDownstreamCopyBoundaries()
    {
        var policy = PolicyText();
        var receipt = ReceiptText();

        foreach (var text in new[] { policy, receipt })
        {
            AssertContainsAll(
                text,
                "deleting an IronDev reference does not delete the external artifact",
                "backups may retain artifacts after primary deletion",
                "CI logs may duplicate artifact content",
                "local developer worktrees may duplicate artifacts",
                "exported review packages may duplicate artifacts",
                "vector deletion must not be assumed from SQL deletion");
        }

        AssertContainsAll(
            policy,
            "External artifact references must avoid credentialed/private query strings.",
            "Retention policy must not claim deletion is complete unless downstream copies are covered by an implementation contract.");
    }

    [TestMethod]
    public void EvidenceArtifactPolicy_DoesNotTreatArtifactsAsAuthority()
    {
        var policy = PolicyText();
        var receipt = ReceiptText();

        foreach (var text in new[] { policy, receipt })
        {
            AssertContainsAll(
                text,
                "Artifact retention policy does not make artifacts safe.",
                "A retained artifact can still leak.",
                "An artifact hash is not safety.",
                "An artifact reference is not validation.",
                "A retained artifact is not approval.",
                "A retained artifact is not policy satisfaction.",
                "A retained artifact is not source-apply authority.",
                "A retained artifact is not workflow continuation authority.",
                "A retained artifact is not release readiness.",
                "A retained artifact is not deployment readiness.",
                "A retained artifact is not rollback authority.",
                "A retained artifact is not retry authority.");
        }
    }

    [TestMethod]
    public void EvidenceArtifactPolicy_DoesNotIntroduceImplementationOrStorageChanges()
    {
        var root = RepositoryRoot();
        var receipt = ReceiptText();

        Assert.IsFalse(File.Exists(Path.Combine(root, "Database", "migrate_h11_evidence_artifact_retention.sql")), "H11 must not add a SQL migration.");
        AssertFileDoesNotContain("Database/migrations.json", "h11");
        AssertFileDoesNotContain("Database/apply-migrations.ps1", "h11");
        AssertFileDoesNotContain("Database/verify-migrations.ps1", "h11");
        AssertFileDoesNotContain("Docs/testing/SLOW_TEST_QUARANTINE_REGISTER.md", "H11_EVIDENCE_ARTIFACT");

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

            var h11Files = Directory
                .EnumerateFiles(directory, "*h11*", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(directory, "*H11*", SearchOption.AllDirectories))
                .Select(path => Path.GetRelativePath(root, path))
                .ToArray();

            Assert.AreEqual(0, h11Files.Length, $"H11 implementation file appeared in forbidden path {relativeDirectory}: {string.Join(", ", h11Files)}");
        }

        AssertContainsAll(
            receipt,
            "H11 does not implement artifact deletion.",
            "H11 does not implement artifact expiry.",
            "H11 does not implement retention deletion.",
            "H11 does not implement artifact lifecycle jobs.",
            "H11 does not implement artifact cleanup commands.",
            "H11 does not add a SQL migration.",
            "H11 does not alter tables.",
            "H11 does not add indexes.",
            "H11 does not alter stored procedures.",
            "H11 does not change API/CLI/UI behavior.",
            "H11 does not change Weaviate behavior.",
            "H11 does not add projection rebuild.",
            "H11 does not add backfill.",
            "H11 does not add replay.");
    }

    [TestMethod]
    public void Receipt_RecordsPolicyScopeAndLimitations()
    {
        var receipt = ReceiptText();

        AssertContainsAll(
            receipt,
            "H11 defines policy only.",
            "Artifact retention policy controls lifecycle only.",
            "Artifact retention policy does not make artifacts safe.",
            "H11 does not scrub existing artifact stores.",
            "H11 does not prove existing artifacts are clean.",
            "H11 does not prove retained artifacts are safe.",
            "H11 does not prove backups, CI logs, local worktrees, exported packages, vector indexes, or external systems are clean.",
            "H11 does not define exact retention day counts because a complete retention calendar is not present yet.",
            "H11 does not implement hold management.",
            "H11 does not implement deletion eligibility.",
            "H11 does not implement H12.",
            "H12 - Backup/rebuild story for read projections.");
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

    private static string PolicyText() =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), PolicyRelativePath));

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
