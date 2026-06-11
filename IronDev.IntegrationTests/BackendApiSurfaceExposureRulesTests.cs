using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BackendApiSurfaceExposureRulesTests
{
    private static readonly string[] DocumentationFiles =
    [
        "Docs/ADR/ADR-008-api-surface-exposure-rules.md",
        "Docs/API_SURFACE_EXPOSURE_RULES.md"
    ];

    private static readonly string[] RequiredSections =
    [
        "## Purpose",
        "## Block F scope",
        "## Frozen backend contracts",
        "## API exposure principles",
        "## Endpoint Classification",
        "## Request/response envelope rules",
        "## Error model rules",
        "## Authentication/authorization posture",
        "## Audit/evidence exposure rules",
        "## Pagination/filtering rules for read APIs",
        "## Human approval rules",
        "## Forbidden endpoint patterns",
        "## Block F PR sequence"
    ];

    private static readonly string[] RequiredInvariants =
    [
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
    ];

    private static readonly string[] RequiredReferences =
    [
        "Docs/BACKEND_CONTRACT_FREEZE_REPORT.md",
        "Docs/BACKEND_ARCHITECTURE.md",
        "Docs/L4_L5_OPERATIONAL_DEBUGGING.md",
        "Docs/ADR/README.md",
        "ADR-001",
        "ADR-002",
        "ADR-003",
        "ADR-004",
        "ADR-005",
        "ADR-006",
        "ADR-007"
    ];

    private static readonly string[] ForbiddenEndpointPatterns =
    [
        "source apply endpoint",
        "memory promotion endpoint",
        "automatic tool execution endpoint",
        "model-output approval endpoint",
        "critic-governance endpoint",
        "audit-approval endpoint",
        "vector-as-truth endpoint",
        "hidden workflow/autonomous runner endpoint",
        "endpoint that combines request + approval + execution"
    ];

    private static readonly string[] ForbiddenBoundaryInversions =
    [
        "api call is approval",
        "cli command is approval",
        "audit is approval",
        "critic is governance",
        "gate is executor",
        "proposal is apply",
        "memory safe is approval",
        "model output is permission",
        "retrieval match is memory candidate"
    ];

    [TestMethod]
    public void BackendApiSurfaceExposureRules_DocumentsExist()
    {
        foreach (var relativePath in DocumentationFiles)
        {
            Assert.IsTrue(File.Exists(Path.Combine(RepositoryRoot(), relativePath)), $"Missing API exposure documentation: {relativePath}");
        }
    }

    [TestMethod]
    public void BackendApiSurfaceExposureRules_ApiRulesContainRequiredSections()
    {
        var rules = ReadRepositoryFile("Docs", "API_SURFACE_EXPOSURE_RULES.md");

        foreach (var section in RequiredSections)
        {
            StringAssert.Contains(rules, section);
        }
    }

    [TestMethod]
    public void BackendApiSurfaceExposureRules_ReferencesFreezeArchitectureAndAdrDocs()
    {
        var combined = ReadCombinedDocumentation();

        foreach (var reference in RequiredReferences)
        {
            StringAssert.Contains(combined, reference);
        }
    }

    [TestMethod]
    public void BackendApiSurfaceExposureRules_ContainsRequiredInvariants()
    {
        var combined = ReadCombinedDocumentation();

        foreach (var invariant in RequiredInvariants)
        {
            StringAssert.Contains(combined, invariant);
        }
    }

    [TestMethod]
    public void BackendApiSurfaceExposureRules_DefinesEndpointClassificationAndForbiddenPatterns()
    {
        var rules = ReadRepositoryFile("Docs", "API_SURFACE_EXPOSURE_RULES.md");

        StringAssert.Contains(rules, "### Read-only inspection endpoints");
        StringAssert.Contains(rules, "### Request creation endpoints");
        StringAssert.Contains(rules, "### Gate evaluation endpoints");
        StringAssert.Contains(rules, "### Forbidden endpoints in Block F");

        foreach (var pattern in ForbiddenEndpointPatterns)
        {
            StringAssert.Contains(rules, pattern);
        }
    }

    [TestMethod]
    public void BackendApiSurfaceExposureRules_DefinesEnvelopeErrorApprovalAuditNamingAndSequenceRules()
    {
        var rules = ReadRepositoryFile("Docs", "API_SURFACE_EXPOSURE_RULES.md");

        foreach (var expected in new[]
        {
            "status",
            "evidenceId",
            "mutationOccurred",
            "humanApprovalRequired",
            "validation error",
            "forbidden by policy/gate",
            "missing human approval",
            "backend contract exception",
            "Audit/evidence exposure rules",
            "API naming rules",
            "PR 58 - Read-only Agent Run API v1",
            "PR 59 - Manual Critic API v1",
            "PR 60 - Manual Memory Improvement API v1",
            "PR 61 - Tool Request API v1",
            "PR 62 - Tool Gate API v1",
            "PR 63 - Dogfood Loop API v1"
        })
        {
            StringAssert.Contains(rules, expected);
        }
    }

    [TestMethod]
    public void BackendApiSurfaceExposureRules_DoesNotInvertCoreBoundaries()
    {
        var combined = ReadCombinedDocumentation().ToLowerInvariant();

        foreach (var forbidden in ForbiddenBoundaryInversions)
        {
            Assert.IsFalse(combined.Contains(forbidden, StringComparison.Ordinal), $"API exposure docs must not contain boundary inversion: {forbidden}");
        }
    }

    [TestMethod]
    public void BackendApiSurfaceExposureRules_DocsAreAsciiNoBomAndNoHiddenUnicode()
    {
        foreach (var relativePath in DocumentationFiles.Concat(["Docs/ADR/README.md"]))
        {
            var path = Path.Combine(RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
            var bytes = File.ReadAllBytes(path);
            AssertAsciiBytesAndNoBom(relativePath, bytes);
            AssertAsciiAndNoFormatControls(relativePath, Encoding.ASCII.GetString(bytes));
        }
    }

    [TestMethod]
    public void BackendApiSurfaceExposureRules_DoesNotAddRuntimeImplementationTokens()
    {
        var combined = ReadCombinedDocumentation();
        var forbidden = new[]
        {
            "ControllerBase",
            "HttpPost",
            "MapPost",
            "WebApplication",
            "AddScoped<",
            "IHostedService",
            "BackgroundService",
            "ProcessStartInfo",
            "CREATE OR ALTER PROCEDURE",
            "ALTER TABLE",
            "DROP TABLE",
            "File.Copy",
            "File.Delete"
        };

        foreach (var token in forbidden)
        {
            Assert.IsFalse(combined.Contains(token, StringComparison.OrdinalIgnoreCase), $"API exposure docs must not introduce implementation token: {token}");
        }
    }

    private static string ReadCombinedDocumentation()
    {
        return string.Join(Environment.NewLine, DocumentationFiles.Select(ReadRepositoryFileByRelativePath).Concat([ReadRepositoryFile("Docs", "ADR", "README.md")]));
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
