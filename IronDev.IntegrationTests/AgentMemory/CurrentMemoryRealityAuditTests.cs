using System.Text.RegularExpressions;

namespace IronDev.IntegrationTests.AgentMemory;

[TestClass]
[TestCategory("StaticBoundary")]
public sealed class CurrentMemoryRealityAuditTests
{
    private static readonly string[] RequiredSections =
    [
        "Tables", "Models", "Services", "Controllers", "Clients", "UI Surfaces",
        "Semantic Providers", "Vector Providers", "Write Paths", "Read Paths",
        "Promotion Paths", "Reindex Paths", "Retrieval Consumers", "Tests", "Docs", "Receipts"
    ];

    private static readonly HashSet<string> AllowedClassifications =
    [
        "ProjectCanon", "OperationalMemory", "SessionMemory", "AgentPrivateMemory",
        "RawEvidence", "Proposal", "DerivedIndex", "Legacy"
    ];

    [TestMethod]
    public void Audit_CoversEveryRequiredInventoryCategory()
    {
        var audit = Audit();
        foreach (var section in RequiredSections)
            StringAssert.Contains(audit, $"## {section}");
    }

    [TestMethod]
    public void Audit_HasNoUnknownClassification()
    {
        var audit = Audit();
        Assert.IsFalse(Regex.IsMatch(audit, @"\|\s*Unknown\s*\|", RegexOptions.IgnoreCase));
        StringAssert.Contains(audit, "Unknown count: 0");
    }

    [TestMethod]
    public void Audit_UsesOnlyTheApprovedClassificationVocabulary()
    {
        var classifications = Regex.Matches(Audit(), @"^\|.*?\|\s*(?<classification>[A-Za-z]+)\s*\|", RegexOptions.Multiline)
            .Select(match => match.Groups["classification"].Value)
            .Where(value => value != "Classification")
            .ToArray();

        Assert.IsNotEmpty(classifications);
        var invalid = classifications.Where(value => !AllowedClassifications.Contains(value)).Distinct().ToArray();
        Assert.IsEmpty(invalid, $"Invalid memory classifications: {string.Join(", ", invalid)}");
    }

    [TestMethod]
    public void Audit_NamesTheActiveAuthorityAndMaintenanceBlockers()
    {
        var audit = Audit();
        StringAssert.Contains(audit, "direct Project Canon-shaped writes");
        StringAssert.Contains(audit, "generic authenticated");
        StringAssert.Contains(audit, "route project/body scope");
        StringAssert.Contains(audit, "maintenance/admin capability");
        StringAssert.Contains(audit, "not registered in the production host");
    }

    private static string Audit() => File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "memory", "CURRENT_MEMORY_REALITY_AUDIT.md"));

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
