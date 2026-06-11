using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BackendAdrDocumentationTests
{
    private static readonly string[] AdrFiles =
    [
        "Docs/ADR/README.md",
        "Docs/ADR/ADR-001-SQL-source-of-truth.md",
        "Docs/ADR/ADR-002-retrieval-match-not-memory-candidate.md",
        "Docs/ADR/ADR-003-memory-candidate-proposal-promotion-boundary.md",
        "Docs/ADR/ADR-004-proposal-review-apply-boundary.md",
        "Docs/ADR/ADR-005-tool-request-audit-execution-boundary.md",
        "Docs/ADR/ADR-006-critic-gate-governance-boundary.md",
        "Docs/ADR/ADR-007-human-review-required-for-apply-and-promotion.md",
        "Docs/ADR/ADR-008-api-surface-exposure-rules.md"
    ];

    [TestMethod]
    public void BackendAdr_IndexAndRequiredFilesExist()
    {
        foreach (var relativePath in AdrFiles)
            Assert.IsTrue(File.Exists(Path.Combine(RepositoryRoot(), relativePath)), $"Missing backend ADR file: {relativePath}");

        var index = ReadRepositoryFile("Docs", "ADR", "README.md");
        foreach (var expected in new[]
        {
            "pre-PR56 Backend Contract Freeze preparation",
            "Backend Architecture",
            "Backend Naming Inventory",
            "Backend SQL Inventory",
            "Backend Inline SQL Inventory",
            "Backend Entity/Table Inventory",
            "Backend Test Fixture Inventory",
            "ADR-001-SQL-source-of-truth.md",
            "ADR-002-retrieval-match-not-memory-candidate.md",
            "ADR-003-memory-candidate-proposal-promotion-boundary.md",
            "ADR-004-proposal-review-apply-boundary.md",
            "ADR-005-tool-request-audit-execution-boundary.md",
            "ADR-006-critic-gate-governance-boundary.md",
            "ADR-007-human-review-required-for-apply-and-promotion.md",
            "ADR-008-api-surface-exposure-rules.md"
        })
        {
            StringAssert.Contains(index, expected);
        }
    }

    [TestMethod]
    public void BackendAdr_DocumentsCoreInvariants()
    {
        var allAdrText = string.Join(Environment.NewLine, AdrFiles.Select(ReadRepositoryFileByRelativePath));

        foreach (var expected in new[]
        {
            "SQL is source of truth",
            "Vector/index/retrieval is retrieval only",
            "Retrieval match is not memory candidate",
            "Candidate is not memory",
            "Proposal is not apply",
            "Audit is not approval",
            "Gate is not executor",
            "Critic is not governance",
            "Memory safe is not approval",
            "Tool request is request form, not execution permission",
            "Model output is advisory only",
            "Human review remains required for source apply and memory promotion"
        })
        {
            StringAssert.Contains(allAdrText, expected);
        }
    }

    [TestMethod]
    public void BackendAdr_EachDecisionLinksToArchitectureAndInventories()
    {
        foreach (var relativePath in AdrFiles.Where(path => path.Contains("ADR-00", StringComparison.Ordinal)))
        {
            var adr = ReadRepositoryFileByRelativePath(relativePath);
            StringAssert.Contains(adr, "../BACKEND_ARCHITECTURE.md");
            StringAssert.Contains(adr, "../BACKEND_SQL_INVENTORY.md");
            StringAssert.Contains(adr, "../BACKEND_ENTITY_TABLE_INVENTORY.md");
            StringAssert.Contains(adr, "../BACKEND_NAMING_INVENTORY.md");
        }
    }

    [TestMethod]
    public void BackendAdr_RecordsRequiredExplicitRejections()
    {
        var allAdrText = string.Join(Environment.NewLine, AdrFiles.Select(ReadRepositoryFileByRelativePath));

        foreach (var expected in new[]
        {
            "Vector index as authority is rejected.",
            "Model output as authority is rejected.",
            "Audit trail as approval is rejected.",
            "Retrieval match as memory is rejected.",
            "Retrieval match becoming candidate memory automatically is rejected.",
            "Automatic memory promotion is rejected.",
            "Safety result acting as approval is rejected.",
            "Audit record acting as promotion is rejected.",
            "Model output directly becoming memory is rejected.",
            "Vector retrieval directly becoming memory is rejected.",
            "Proposal-as-apply is rejected.",
            "Critic-as-approval is rejected.",
            "Audit-as-approval is rejected.",
            "Model-output-as-source-mutation is rejected.",
            "Request as permission is rejected.",
            "Audit as approval is rejected.",
            "Audit as executor is rejected.",
            "Gate as executor is rejected.",
            "Critic as governor is rejected.",
            "Model confidence as approval is rejected.",
            "Advisory text as permission is rejected.",
            "Automatic source apply is rejected.",
            "Hidden approval through model/tool output is rejected.",
            "Safe classification becoming approval is rejected."
        })
        {
            StringAssert.Contains(allAdrText, expected);
        }
    }

    [TestMethod]
    public void BackendAdr_DoesNotClaimRuntimeSchemaOrCapabilityChanges()
    {
        var allAdrText = string.Join(Environment.NewLine, AdrFiles.Select(ReadRepositoryFileByRelativePath));
        var forbidden = new[]
        {
            "HttpPost",
            "ControllerBase",
            "WebApplication",
            "AddScoped<",
            "IHostedService",
            "BackgroundService",
            "ProcessStartInfo",
            "File.WriteAllText",
            "NOCHECK CONSTRAINT",
            "DISABLE TRIGGER",
            "CREATE OR ALTER PROCEDURE",
            "ALTER TABLE",
            "DROP TABLE",
            "PromoteCollectiveMemory",
            "SubmitReview"
        };

        foreach (var token in forbidden)
        {
            Assert.IsFalse(
                allAdrText.Contains(token, StringComparison.OrdinalIgnoreCase),
                $"Backend ADR documentation must not introduce runtime/schema/capability token: {token}");
        }
    }

    [TestMethod]
    public void BackendAdr_DocsDoNotContainHiddenOrBidirectionalUnicode()
    {
        foreach (var relativePath in AdrFiles)
        {
            var absolutePath = Path.Combine(RepositoryRoot(), relativePath);
            AssertAsciiBytesAndNoBom(relativePath, File.ReadAllBytes(absolutePath));
            AssertAsciiAndNoFormatControls(relativePath, File.ReadAllText(absolutePath));
        }
    }

    private static string ReadRepositoryFile(params string[] pathParts) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), Path.Combine(pathParts)));

    private static string ReadRepositoryFileByRelativePath(string relativePath) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static void AssertAsciiAndNoFormatControls(string path, string source)
    {
        for (var index = 0; index < source.Length; index++)
        {
            var current = source[index];
            var category = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(current);
            if (current > 127 || category == System.Globalization.UnicodeCategory.Format)
                Assert.Fail($"{path} contains hidden or non-ASCII Unicode at index {index}: U+{(int)current:X4}.");
        }
    }

    private static void AssertAsciiBytesAndNoBom(string path, byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            Assert.Fail($"{path} must not contain a UTF-8 byte-order mark.");

        for (var index = 0; index < bytes.Length; index++)
        {
            if (bytes[index] > 127)
                Assert.Fail($"{path} contains non-ASCII byte at offset {index}: 0x{bytes[index]:X2}.");
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;

        if (directory is null)
            throw new InvalidOperationException("Could not locate repository root.");

        return directory.FullName;
    }
}
