using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("Governance")]
[TestCategory("PayloadSafety")]
[TestCategory("Redaction")]
[TestCategory("Retention")]
[TestCategory("Policy")]
[TestCategory("Boundary")]
[TestCategory("Contract")]
public sealed class RawPayloadRedactionRetentionPolicyTests
{
    private const string PolicyRelativePath = "Docs/policies/H10_RAW_PAYLOAD_REDACTION_RETENTION_POLICY.md";
    private const string ReceiptRelativePath = "Docs/receipts/H10_RAW_PAYLOAD_REDACTION_RETENTION_POLICY.md";

    private static readonly string[] RequiredClasses =
    [
        "StoreProhibited",
        "RedactBeforeStore",
        "ReferenceOnly",
        "SafeSummaryAllowed",
        "GovernanceEvidenceRetained",
        "TemporaryDiagnostic"
    ];

    private static readonly string[] RequiredUnsafeMarkers =
    [
        "password",
        "secret",
        "token",
        "bearer",
        "api key",
        "private key",
        "connection string",
        "credential",
        "authorization header",
        "cookie",
        ".env",
        "raw production data",
        "raw customer/person data",
        "raw prompt/context dump"
    ];

    private static readonly string[] KnownPayloadSurfaces =
    [
        "PayloadJson",
        "EvidenceJson",
        "CommandAuditsJson",
        "EvidenceReferencesJson",
        "BoundaryMaximsJson",
        "BoundaryText",
        "FileResultsJson",
        "IssueCodesJson",
        "SourceUri",
        "Summary",
        "EvidenceLabel",
        "EvidenceSummary",
        "SafeSummary",
        "AllowedUse"
    ];

    [TestMethod]
    public void RawPayloadPolicy_DefinesPayloadClassesAndRetentionClasses()
    {
        var policy = PolicyText();
        var receipt = ReceiptText();

        foreach (var requiredClass in RequiredClasses)
        {
            StringAssert.Contains(policy, $"`{requiredClass}`");
            StringAssert.Contains(receipt, $"`{requiredClass}`");
        }

        AssertContainsAll(
            policy,
            "## 3. Raw Payload Classification",
            "## 4. Retention Policy",
            "| `StoreProhibited` | Do not retain | Reject or redact before storage |",
            "| `TemporaryDiagnostic` | Short-lived only | Future deletion/expiry implementation required |");
    }

    [TestMethod]
    public void RawPayloadPolicy_DefinesRequiredUnsafeMaterialMarkers()
    {
        var policy = PolicyText();
        var receipt = ReceiptText();

        foreach (var marker in RequiredUnsafeMarkers)
        {
            StringAssert.Contains(policy, marker);
            StringAssert.Contains(receipt, marker);
        }

        AssertContainsAll(
            policy,
            "refresh token",
            "access token",
            "client secret",
            "SSH key",
            "certificate private key",
            "environment dump",
            "raw email body",
            "raw legal/regulatory data",
            "This list is not exhaustive. Unknown sensitive material is still sensitive even if the marker list misses it.");
    }

    [TestMethod]
    public void RawPayloadPolicy_ClassifiesKnownPayloadSurfaces()
    {
        var policy = PolicyText();

        foreach (var surface in KnownPayloadSurfaces)
            StringAssert.Contains(policy, $"`{surface}`");

        AssertContainsAll(
            policy,
            "patch artifact content/hash/reference surfaces",
            "tool request payload surfaces",
            "governance event payload surfaces",
            "workflow/handoff/memory proposal evidence reference surfaces",
            "run report / trace surfaces",
            "Weaviate/vector-indexed text",
            "If a listed surface is already safe-summary/reference-only by design, this policy preserves that classification.");
    }

    [TestMethod]
    public void RawPayloadPolicy_RequiresRedactionBeforeStorageOrDisplay()
    {
        var policy = PolicyText();

        AssertContainsAll(
            policy,
            "Redact before durable storage where possible.",
            "Redact before display where storage still contains raw/private material.",
            "Never rely on UI-only redaction as storage safety.",
            "Never rely on vector retrieval filters as redaction.",
            "Never include raw private payloads in receipts just because receipts are append-only.",
            "Never include raw private payloads in governance events just because events are append-only.",
            "Append-only storage does not make raw payloads safe.",
            "Redacted display is not proof storage is redacted.");
    }

    [TestMethod]
    public void RawPayloadPolicy_DefersArtifactRetentionToH11()
    {
        var policy = PolicyText();
        var receipt = ReceiptText();

        foreach (var text in new[] { policy, receipt })
        {
            AssertContainsAll(
                text,
                "H11 owns evidence artifact retention policy",
                "H10 does not implement evidence artifact retention",
                "Artifact retention policy controls lifecycle. It does not make artifacts safe.",
                "A retained artifact can still leak.");
        }

        AssertContainsAll(
            policy,
            "H10 must not implement artifact deletion, artifact expiry, artifact lifecycle jobs, or retention jobs.",
            "H10 does not implement artifact lifecycle behavior.");
    }

    [TestMethod]
    public void RawPayloadPolicy_DoesNotTreatRetentionAsAuthority()
    {
        var policy = PolicyText();
        var receipt = ReceiptText();

        foreach (var text in new[] { policy, receipt })
        {
            AssertContainsAll(
                text,
                "A retained secret is still a secret.",
                "A retained payload is not approval.",
                "A retained payload is not policy satisfaction.",
                "A retained payload is not source-apply authority.",
                "A retained payload is not workflow continuation authority.",
                "A retained payload is not release readiness.",
                "A retained payload is not deployment readiness.",
                "A retained payload does not prove the payload is true.",
                "A retained payload does not prove the actor was authorized.",
                "A retained payload does not prove the next action is safe.");
        }

        AssertContainsAll(
            policy,
            "Retention is evidence handling, not authority.",
            "Existing payloads are not clean merely because this policy exists.",
            "Redaction means exposure is limited. It does not mean the original never existed.");
    }

    [TestMethod]
    public void RawPayloadPolicy_DoesNotIntroduceImplementationOrStorageChanges()
    {
        var root = RepositoryRoot();
        var receipt = ReceiptText();

        Assert.IsFalse(File.Exists(Path.Combine(root, "Database", "migrate_h10_raw_payload_redaction_retention.sql")), "H10 must not add a SQL migration.");
        AssertFileDoesNotContain("Database/migrations.json", "h10");
        AssertFileDoesNotContain("Database/apply-migrations.ps1", "h10");
        AssertFileDoesNotContain("Database/verify-migrations.ps1", "h10");
        AssertFileDoesNotContain("Docs/testing/SLOW_TEST_QUARANTINE_REGISTER.md", "H10_RAW_PAYLOAD");

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

            var h10Files = Directory
                .EnumerateFiles(directory, "*h10*", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(directory, "*H10*", SearchOption.AllDirectories))
                .Select(path => Path.GetRelativePath(root, path))
                .ToArray();

            Assert.AreEqual(0, h10Files.Length, $"H10 implementation file appeared in forbidden path {relativeDirectory}: {string.Join(", ", h10Files)}");
        }

        AssertContainsAll(
            receipt,
            "H10 does not implement redaction.",
            "H10 does not implement retention deletion.",
            "H10 does not add a SQL migration.",
            "H10 does not alter tables.",
            "H10 does not add indexes.",
            "H10 does not alter stored procedures.",
            "H10 does not change API/CLI/UI behavior.",
            "H10 does not change Weaviate behavior.",
            "H10 does not add projection rebuild.",
            "H10 does not add backfill.",
            "H10 does not add replay.");
    }

    [TestMethod]
    public void Receipt_RecordsPolicyScopeAndLimitations()
    {
        var receipt = ReceiptText();

        AssertContainsAll(
            receipt,
            "H10 defines policy only.",
            "Redaction policy limits exposure only.",
            "Retention policy does not make retained payloads safe.",
            "H10 does not implement evidence artifact retention.",
            "H10 does not change workflow/source-apply/rollback/release/deployment authority.",
            "H10 does not sanitize historical data.",
            "H10 does not prove existing records are clean.",
            "H10 does not remove already-retained secrets.",
            "H10 does not prove backups, traces, artifacts, logs, vector indexes, or downstream copies are clean.",
            "H10 does not implement H11.");
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
