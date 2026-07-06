using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Demo;

[TestClass]
[TestCategory("DemoRehearsal")]
[TestCategory("DogfoodGate")]
[TestCategory("BookSellerBatch")]
[TestCategory("AlphaSmoke")]
[TestCategory("ReleaseReadiness")]
[TestCategory("Contract")]
[TestCategory("Boundary")]
public sealed class DemoDogfoodGateArtifactTests
{
    private static readonly string[] AllowedDemo7Verdicts =
    [
        "DemoReady",
        "Blocked",
        "RepeatabilityFailed",
        "SafetyBlocked",
        "PersistenceBlocked",
        "UxBlocked",
        "DocsBlocked"
    ];

    private static readonly string[] AllowedGateVerdicts =
    [
        "GoForLocalAlphaPreview",
        "NoGoBlocked",
        "ConditionalGoWithNamedLimitations",
        "EvidenceIncomplete"
    ];

    private static readonly string[] AllowedTicketVerdicts =
    [
        "TicketApplied",
        "TicketPausedForApproval",
        "TicketBlocked",
        "TicketFailed",
        "TicketDescoped"
    ];

    private static readonly string[] AllowedBatchVerdicts =
    [
        "BatchPassed",
        "BatchBlocked",
        "BatchFailed",
        "BatchPartiallyPassed",
        "BatchEvidenceIncomplete"
    ];

    [TestMethod]
    public void DemoRehearsal001_TranscriptExists()
    {
        var text = Read("Docs", "dogfood", "DEMO-REHEARSAL-001.md");

        StringAssert.Contains(text, "# DEMO-REHEARSAL-001");
        StringAssert.Contains(text, "Verdict: `Blocked`");
        StringAssert.Contains(text, "NonAuthorOperatorMissing");
    }

    [TestMethod]
    public void DemoRehearsal001_UsesAllowedVerdictVocabulary()
    {
        var text = Read("Docs", "dogfood", "DEMO-REHEARSAL-001.md");
        var verdict = ExtractBacktickValue(text, "Verdict:");
        var repeatability = ExtractBacktickValue(text, "Repeatability Verdict");

        CollectionAssert.Contains(AllowedDemo7Verdicts, verdict);
        CollectionAssert.Contains(AllowedDemo7Verdicts, repeatability);
    }

    [TestMethod]
    public void DemoRehearsal001_ContainsRequiredFields()
    {
        var text = Read("Docs", "dogfood", "DEMO-REHEARSAL-001.md");

        foreach (var required in new[]
                 {
                     "Commit SHA",
                     "OS/shell",
                     ".NET version",
                     "Node/npm version",
                     "Git version",
                     "SQL status",
                     "Weaviate status",
                     "Root safety status",
                     "API start command",
                     "UI start command",
                     "Model mode",
                     "Project ID",
                     "Seeded ticket IDs",
                     "Live ticket ID",
                     "Run ID",
                     "Critic package hash",
                     "Approval ID",
                     "Continuation result",
                     "Apply receipt path/hash",
                     "Final report path",
                     "Blockers",
                     "Manual Fixes",
                     "Repeatability Verdict"
                 })
        {
            StringAssert.Contains(text, required);
        }
    }

    [TestMethod]
    public void DemoRehearsal001_RecordsWhyNotDemoReady()
    {
        var text = Read("Docs", "dogfood", "DEMO-REHEARSAL-001.md");

        StringAssert.Contains(text, "fresh non-author operator has not yet executed");
        StringAssert.Contains(text, "It does not prove the demo is repeatable.");
        AssertDoesNotContain(text, "Verdict: `DemoReady`");
    }

    [TestMethod]
    public void DemoRehearsal001_DoesNotLeakSecretsOrUserLocalPaths()
    {
        AssertNoSecretOrUserPathLeak(Read("Docs", "dogfood", "DEMO-REHEARSAL-001.md"));
    }

    [TestMethod]
    public void DogfoodAlphaLocal001_TranscriptExists()
    {
        var text = Read("Docs", "release", "v0.1-local-alpha", "DOGFOOD-ALPHA-LOCAL-001.md");

        StringAssert.Contains(text, "# DOGFOOD-ALPHA-LOCAL-001");
        StringAssert.Contains(text, "Verdict: `EvidenceIncomplete`");
        StringAssert.Contains(text, "Gate Blockers");
    }

    [TestMethod]
    public void DogfoodAlphaLocal001_JsonHasStableGateFields()
    {
        using var document = ReadJson("Docs", "release", "v0.1-local-alpha", "DOGFOOD-ALPHA-LOCAL-001.json");
        var root = document.RootElement;

        foreach (var field in new[]
                 {
                     "gateId",
                     "commitSha",
                     "branch",
                     "startedAtUtc",
                     "completedAtUtc",
                     "verdict",
                     "doctorStatus",
                     "alphaSmokeStatus",
                     "finalRunState",
                     "backendEvidenceRefs",
                     "receiptRefs",
                     "knownLimitations",
                     "gateBlockers",
                     "nextSafeAction",
                     "boundary"
                 })
        {
            Assert.IsTrue(root.TryGetProperty(field, out _), $"Missing DOGFOOD gate field: {field}");
        }

        Assert.AreEqual("DOGFOOD-ALPHA-LOCAL-001", root.GetProperty("gateId").GetString());
        Assert.AreEqual(40, root.GetProperty("commitSha").GetString()!.Length);
        Assert.AreEqual("EvidenceIncomplete", root.GetProperty("verdict").GetString());
    }

    [TestMethod]
    public void DogfoodAlphaLocal001_UsesAllowedVerdictVocabulary()
    {
        using var document = ReadJson("Docs", "release", "v0.1-local-alpha", "DOGFOOD-ALPHA-LOCAL-001.json");

        CollectionAssert.Contains(AllowedGateVerdicts, document.RootElement.GetProperty("verdict").GetString());
    }

    [TestMethod]
    public void DogfoodAlphaLocal001_ListsCommandsInOrder()
    {
        var text = Read("Docs", "release", "v0.1-local-alpha", "DOGFOOD-ALPHA-LOCAL-001.md");

        AssertBefore(text, "1. `git rev-parse HEAD`", "2. `Scripts/local/doctor-local.ps1 -CheckOnly -Json`");
        AssertBefore(text, "2. `Scripts/local/doctor-local.ps1 -CheckOnly -Json`", "3. `Scripts/local/bootstrap-local.ps1 -CheckOnly`");
        AssertBefore(text, "3. `Scripts/local/bootstrap-local.ps1 -CheckOnly`", "4. `Scripts/demo/start-v0.1-demo.ps1 -CheckOnly -NoStart -Json`");
        AssertBefore(text, "4. `Scripts/demo/start-v0.1-demo.ps1 -CheckOnly -NoStart -Json`", "5. `Scripts/demo/demo-seed.ps1 -CheckOnly -Json`");
        AssertBefore(text, "5. `Scripts/demo/demo-seed.ps1 -CheckOnly -Json`", "6. `Scripts/demo/start-v0.1-demo.ps1`");
    }

    [TestMethod]
    public void DogfoodAlphaLocal001_NamesDoctorSmokeAndFinalState()
    {
        using var document = ReadJson("Docs", "release", "v0.1-local-alpha", "DOGFOOD-ALPHA-LOCAL-001.json");
        var root = document.RootElement;

        Assert.AreEqual("NotRunInThisArtifact", root.GetProperty("doctorStatus").GetString());
        Assert.AreEqual("NotRunInThisArtifact", root.GetProperty("alphaSmokeStatus").GetString());
        Assert.AreEqual("NotEstablishedByThisArtifact", root.GetProperty("finalRunState").GetString());
    }

    [TestMethod]
    public void DogfoodAlphaLocal001_SeparatesEvidenceFromAuthority()
    {
        var text = Read("Docs", "release", "v0.1-local-alpha", "DOGFOOD-ALPHA-LOCAL-001.md");
        using var document = ReadJson("Docs", "release", "v0.1-local-alpha", "DOGFOOD-ALPHA-LOCAL-001.json");

        StringAssert.Contains(text, "release evidence only");
        StringAssert.Contains(text, "does not approve");
        StringAssert.Contains(text, "does not grant release authority");
        StringAssert.Contains(document.RootElement.GetProperty("boundary").GetString()!, "does not approve");
        AssertDoesNotContain(text, "ShipIt");
        AssertDoesNotContain(text, "ProbablyReady");
    }

    [TestMethod]
    public void DogfoodAlphaLocal001_DoesNotLeakSecretsOrUserLocalPaths()
    {
        AssertNoSecretOrUserPathLeak(Read("Docs", "release", "v0.1-local-alpha", "DOGFOOD-ALPHA-LOCAL-001.md"));
        AssertNoSecretOrUserPathLeak(Read("Docs", "release", "v0.1-local-alpha", "DOGFOOD-ALPHA-LOCAL-001.json"));
    }

    [TestMethod]
    public void DogfoodAlphaLocal001_DoesNotClaimUiJourneyAsProven()
    {
        var text = Read("Docs", "release", "v0.1-local-alpha", "DOGFOOD-ALPHA-LOCAL-001.md");
        using var document = ReadJson("Docs", "release", "v0.1-local-alpha", "DOGFOOD-ALPHA-LOCAL-001.json");

        StringAssert.Contains(text, "No fresh UI walk was executed");
        StringAssert.Contains(text, "not a fresh local UI dogfood run");
        AssertJsonArrayContains(document.RootElement.GetProperty("knownLimitations"), "UI journey evidence remains mocked API Playwright proof");
    }

    [TestMethod]
    public void DogfoodAlphaLocal001_KnownLimitationsAndBlockersAreExplicit()
    {
        using var document = ReadJson("Docs", "release", "v0.1-local-alpha", "DOGFOOD-ALPHA-LOCAL-001.json");
        var root = document.RootElement;

        Assert.IsTrue(root.GetProperty("knownLimitations").GetArrayLength() >= 3);
        Assert.IsTrue(root.GetProperty("gateBlockers").GetArrayLength() >= 3);
        AssertJsonArrayContains(root.GetProperty("gateBlockers"), "NonAuthorRehearsalNotRun");
        AssertJsonArrayContains(root.GetProperty("gateBlockers"), "DogfoodGateNotExecuted");
    }

    [TestMethod]
    public void DogfoodAlphaLocal001_GoVerdictRequiresNoGateBlockers()
    {
        using var document = ReadJson("Docs", "release", "v0.1-local-alpha", "DOGFOOD-ALPHA-LOCAL-001.json");
        var root = document.RootElement;
        var verdict = root.GetProperty("verdict").GetString();
        var blockerCount = root.GetProperty("gateBlockers").GetArrayLength();

        if (verdict == "GoForLocalAlphaPreview")
            Assert.AreEqual(0, blockerCount, "A go verdict cannot carry gate blockers.");
        else
            Assert.AreNotEqual("GoForLocalAlphaPreview", verdict);
    }

    [TestMethod]
    public void BookSellerBatch001_TranscriptExists()
    {
        var text = Read("Docs", "release", "v0.1-local-alpha", "DOGFOOD-BOOKSELLER-BATCH-001.md");

        StringAssert.Contains(text, "# DOGFOOD-BOOKSELLER-BATCH-001");
        StringAssert.Contains(text, "Aggregate verdict: `BatchEvidenceIncomplete`");
    }

    [TestMethod]
    public void BookSellerBatch001_JsonListsExactlyThreeTickets()
    {
        using var document = ReadJson("Docs", "release", "v0.1-local-alpha", "DOGFOOD-BOOKSELLER-BATCH-001.json");
        var tickets = document.RootElement.GetProperty("tickets").EnumerateArray().ToArray();

        Assert.AreEqual(3, tickets.Length);
        CollectionAssert.AreEquivalent(
            new[] { "validate-book", "normalise-book-metadata", "reject-duplicate-isbn" },
            tickets.Select(ticket => ticket.GetProperty("ticketKey").GetString()).ToArray());
    }

    [TestMethod]
    public void BookSellerBatch001_DoesNotInventRunIdsWhenEvidenceIncomplete()
    {
        using var document = ReadJson("Docs", "release", "v0.1-local-alpha", "DOGFOOD-BOOKSELLER-BATCH-001.json");
        var root = document.RootElement;

        Assert.AreEqual("BatchEvidenceIncomplete", root.GetProperty("aggregateVerdict").GetString());
        foreach (var ticket in root.GetProperty("tickets").EnumerateArray())
        {
            Assert.AreEqual(JsonValueKind.Null, ticket.GetProperty("runId").ValueKind);
            Assert.AreEqual(JsonValueKind.Null, ticket.GetProperty("approvalEvidenceRef").ValueKind);
            Assert.AreEqual(JsonValueKind.Null, ticket.GetProperty("applyReceiptRef").ValueKind);
            Assert.AreEqual(JsonValueKind.Null, ticket.GetProperty("reportRef").ValueKind);
        }
    }

    [TestMethod]
    public void BookSellerBatch001_AggregateVerdictCannotHideTicketFailure()
    {
        using var document = ReadJson("Docs", "release", "v0.1-local-alpha", "DOGFOOD-BOOKSELLER-BATCH-001.json");
        var root = document.RootElement;
        var aggregate = root.GetProperty("aggregateVerdict").GetString();
        var tickets = root.GetProperty("tickets").EnumerateArray().ToArray();
        var anyNotApplied = tickets.Any(ticket => ticket.GetProperty("ticketVerdict").GetString() != "TicketApplied");

        if (anyNotApplied)
            Assert.AreNotEqual("BatchPassed", aggregate, "BatchPassed cannot hide non-applied tickets.");
    }

    [TestMethod]
    public void BookSellerBatch001_UsesAllowedVerdictVocabulary()
    {
        using var document = ReadJson("Docs", "release", "v0.1-local-alpha", "DOGFOOD-BOOKSELLER-BATCH-001.json");
        var root = document.RootElement;

        CollectionAssert.Contains(AllowedBatchVerdicts, root.GetProperty("aggregateVerdict").GetString());
        foreach (var ticket in root.GetProperty("tickets").EnumerateArray())
            CollectionAssert.Contains(AllowedTicketVerdicts, ticket.GetProperty("ticketVerdict").GetString());
    }

    [TestMethod]
    public void BookSellerBatch001_DoesNotClaimReleaseReadiness()
    {
        var text = Read("Docs", "release", "v0.1-local-alpha", "DOGFOOD-BOOKSELLER-BATCH-001.md");
        using var document = ReadJson("Docs", "release", "v0.1-local-alpha", "DOGFOOD-BOOKSELLER-BATCH-001.json");

        StringAssert.Contains(text, "not release readiness");
        StringAssert.Contains(text, "does not replace DOGFOOD-ALPHA-LOCAL-001");
        StringAssert.Contains(document.RootElement.GetProperty("boundary").GetString()!, "not release readiness");
        AssertDoesNotContain(text, "BatchPassed");
    }

    [TestMethod]
    public void BookSellerBatch001_DoesNotLeakSecretsOrUserLocalPaths()
    {
        AssertNoSecretOrUserPathLeak(Read("Docs", "release", "v0.1-local-alpha", "DOGFOOD-BOOKSELLER-BATCH-001.md"));
        AssertNoSecretOrUserPathLeak(Read("Docs", "release", "v0.1-local-alpha", "DOGFOOD-BOOKSELLER-BATCH-001.json"));
    }

    [TestMethod]
    public void BookSellerBatch001_ReceiptAndSpecSayBatchIsOptional()
    {
        var spec = Read("Docs", "release", "v0.1-local-alpha", "DEMO_REAL_SPEC.md");
        var receipt = Read("Docs", "receipts", "DEMO7_9_DOGFOOD_GATE_AND_BATCH.md");

        StringAssert.Contains(spec, "DEMO-9 - Optional BookSeller Three-Ticket Batch Artifact");
        StringAssert.Contains(receipt, "optional BookSeller three-ticket batch");
        StringAssert.Contains(receipt, "No fake run IDs");
        StringAssert.Contains(receipt, "No release readiness claimed");
    }

    [TestMethod]
    public void DemoRealSpec_DefinesDemo8AndDemo9()
    {
        var spec = Read("Docs", "release", "v0.1-local-alpha", "DEMO_REAL_SPEC.md");

        StringAssert.Contains(spec, "DEMO-8  DOGFOOD-ALPHA-LOCAL-001 release gate artifact.");
        StringAssert.Contains(spec, "DEMO-9  Optional BookSeller three-ticket batch artifact.");
        StringAssert.Contains(spec, "If the gate is not actually executed, the verdict must be EvidenceIncomplete");
        StringAssert.Contains(spec, "If DEMO-8 is EvidenceIncomplete, DEMO-9 must stay BatchEvidenceIncomplete");
    }

    [TestMethod]
    public void DemoDogfoodReceiptDocumentsAllBoundaries()
    {
        var receipt = Read("Docs", "receipts", "DEMO7_9_DOGFOOD_GATE_AND_BATCH.md");

        StringAssert.Contains(receipt, "These are not green verdicts.");
        StringAssert.Contains(receipt, "No product code changed.");
        StringAssert.Contains(receipt, "No release, tag, publish, upload, or deploy action.");
        StringAssert.Contains(receipt, "Evidence is not authority.");
        StringAssert.Contains(receipt, "An incomplete gate that says incomplete is useful.");
    }

    private static JsonDocument ReadJson(params string[] path) =>
        JsonDocument.Parse(Read(path));

    private static string Read(params string[] path) =>
        File.ReadAllText(Path.Combine(RepoRoot(), Path.Combine(path)));

    private static string RepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "IronDev.slnx")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate IronDev.slnx.");
    }

    private static string ExtractBacktickValue(string text, string label)
    {
        var labelIndex = text.IndexOf(label, StringComparison.Ordinal);
        Assert.IsTrue(labelIndex >= 0, $"Missing label: {label}");
        var start = text.IndexOf('`', labelIndex);
        var end = text.IndexOf('`', start + 1);
        Assert.IsTrue(start >= 0 && end > start, $"Missing backtick value after label: {label}");
        return text[(start + 1)..end];
    }

    private static void AssertBefore(string text, string earlier, string later)
    {
        var earlierIndex = text.IndexOf(earlier, StringComparison.Ordinal);
        var laterIndex = text.IndexOf(later, StringComparison.Ordinal);

        Assert.IsTrue(earlierIndex >= 0, $"Expected text to contain '{earlier}'.");
        Assert.IsTrue(laterIndex >= 0, $"Expected text to contain '{later}'.");
        Assert.IsTrue(earlierIndex < laterIndex, $"Expected '{earlier}' to appear before '{later}'.");
    }

    private static void AssertJsonArrayContains(JsonElement array, string expectedFragment)
    {
        var found = array.EnumerateArray()
            .Any(item => item.GetString()?.Contains(expectedFragment, StringComparison.Ordinal) == true);

        Assert.IsTrue(found, $"Expected JSON array to contain text: {expectedFragment}");
    }

    private static void AssertNoSecretOrUserPathLeak(string text)
    {
        foreach (var forbidden in new[]
                 {
                     string.Concat(@"C:\", "Users"),
                     string.Concat(@"C:\\", "Users"),
                     "/Users/",
                     "/home/",
                     "password=",
                     "token=",
                     "Bearer ",
                     "gho_",
                     "ghp_",
                     "RawConnectionString"
                 })
        {
            AssertDoesNotContain(text, forbidden);
        }
    }

    private static void AssertDoesNotContain(string text, string forbidden) =>
        Assert.IsFalse(text.Contains(forbidden, StringComparison.OrdinalIgnoreCase), $"Forbidden text found: {forbidden}");
}
