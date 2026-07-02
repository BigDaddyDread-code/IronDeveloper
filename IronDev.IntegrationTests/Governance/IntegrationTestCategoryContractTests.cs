using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestCategory("Governance")]
[TestCategory("Contract")]
[TestCategory("Boundary")]
[TestClass]
public sealed partial class IntegrationTestCategoryContractTests
{
    private static readonly Regex CategoryAttributeRegex = new(@"\[TestCategory\(""([^""]*)""\)\]", RegexOptions.Compiled);
    private static readonly Regex ClassLineRegex = new(@"\bclass\s+([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);
    private static readonly Regex CiCategoryFilterRegex = new(@"TestCategory=([A-Za-z0-9_.:-]+)", RegexOptions.Compiled);
    private static readonly Lazy<IReadOnlyList<SourceFile>> SourceFileCache = new(LoadIntegrationSourceFiles);
    private static readonly Lazy<IReadOnlyList<string>> CategoryNameCache = new(LoadAllCategoryNames);
    private static readonly Lazy<IReadOnlyList<TestClassRecord>> TestClassRecordCache = new(LoadTestClassRecords);

    private static readonly string[] ForbiddenCategoryFragments =
    [
        "Optional",
        "Skip",
        "Ignore",
        "DoNotRun",
        "Quarantined",
        "ManualOnly",
        "NightlyOnly",
        "Flaky",
        "Experimental",
        "Wip"
    ];

    [TestMethod]
    public void CategoryNames_AreNonEmptyTrimmedAndDoNotHideTests()
    {
        foreach (var category in AllCategoryNames())
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(category), "TestCategory must not be empty.");
            Assert.AreEqual(category.Trim(), category, $"TestCategory must not have leading/trailing whitespace: '{category}'.");

            foreach (var forbidden in ForbiddenCategoryFragments)
                Assert.IsFalse(category.Contains(forbidden, StringComparison.OrdinalIgnoreCase), $"Forbidden G13 category fragment '{forbidden}' found in '{category}'.");
        }
    }

    [TestMethod]
    public void TestClassCategoryBlocks_DoNotHaveClassLevelIgnoreOrDuplicateCategories()
    {
        foreach (var record in TestClassRecords())
        {
            Assert.IsFalse(record.AttributeBlock.Contains("[Ignore", StringComparison.Ordinal), $"Test class must not carry [Ignore]: {record.RelativePath}::{record.ClassName}");

            var duplicates = record.Categories
                .GroupBy(category => category, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToArray();

            Assert.AreEqual(0, duplicates.Length, $"Duplicate TestCategory on {record.RelativePath}::{record.ClassName}: {string.Join(", ", duplicates)}");
        }
    }

    [TestMethod]
    public void ExistingMethodLevelIgnoreDebt_IsExplicitAndDoesNotGrow()
    {
        var ignoreHits = IntegrationSourceFiles()
            .SelectMany(file => File.ReadLines(file.AbsolutePath)
                .Select((line, index) => new IgnoreHit(file.RelativePath, index + 1, line.Trim())))
            .Where(hit => hit.Line.StartsWith("[Ignore", StringComparison.Ordinal))
            .ToArray();

        Assert.AreEqual(1, ignoreHits.Length, "G13 must not introduce new [Ignore] attributes. The single existing manual local indexing task remains legacy debt.");
        Assert.AreEqual("IronDev.IntegrationTests/ManualIndexingTask.cs", ignoreHits[0].RelativePath);
        StringAssert.Contains(ignoreHits[0].Line, "Manual local indexing task");
    }

    [TestMethod]
    public void GovernanceBoundaryCi_TestCategoryFiltersStillResolve()
    {
        var script = File.ReadAllText(Path.Combine(RepositoryRoot(), "Scripts", "ci", "run-governance-boundary-ci.ps1"));
        var ciCategories = CiCategoryFilterRegex
            .Matches(script)
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(category => category, StringComparer.Ordinal)
            .ToArray();
        var categories = AllCategoryNames().ToHashSet(StringComparer.Ordinal);

        CollectionAssert.Contains(ciCategories, "ApiCliContract");
        CollectionAssert.Contains(ciCategories, "ApiCliReleaseGate");

        foreach (var category in ciCategories)
            Assert.IsTrue(categories.Contains(category), $"CI-facing category filter does not match any integration test source category: {category}");
    }

    [TestMethod]
    public void StaticBoundaryReceiptDatabaseAndApiCliTests_KeepSelectionMetadata()
    {
        foreach (var record in TestClassRecords())
        {
            if (record.ClassName.Contains("StaticBoundary", StringComparison.Ordinal) || record.RelativePath.Contains("StaticBoundary", StringComparison.Ordinal))
                AssertHasCategoryFragment(record, "Boundary");

            if (record.ClassName.Contains("Receipt", StringComparison.Ordinal) || record.RelativePath.Contains("Receipt", StringComparison.Ordinal))
                AssertHasCategoryFragment(record, "Receipt");

            if (record.ClassName.EndsWith("StoreTests", StringComparison.Ordinal) || record.Categories.Any(category => category.Contains("RealDatabase", StringComparison.Ordinal)))
                Assert.IsTrue(record.Categories.Any(IsRealDatabaseStoreSmokeCategory), $"Store/real-database test class must keep Store/RealDatabase/Smoke category metadata: {record.RelativePath}::{record.ClassName}");

            if (record.RelativePath.StartsWith("IronDev.IntegrationTests/ApiCliContract/", StringComparison.Ordinal))
                Assert.IsTrue(record.Categories.Contains("ApiCliContract") || record.Categories.Contains("ApiCliReleaseGate"), $"API/CLI contract test must remain in ApiCliContract or ApiCliReleaseGate: {record.RelativePath}::{record.ClassName}");
        }
    }

    [TestMethod]
    public void CategoryInventory_DocumentsEveryCurrentCategory()
    {
        var inventory = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "testing", "INTEGRATION_TEST_CATEGORIES.md"));

        foreach (var category in AllCategoryNames())
            StringAssert.Contains(inventory, $"| `{category}` |", $"Category inventory must list category '{category}'.");

        StringAssert.Contains(inventory, "Test categories are not test quality.");
        StringAssert.Contains(inventory, "A label does not make a slow test safe.");
    }

    [TestMethod]
    public void G13AddedCategories_AreBroadMetadataNotAuthority()
    {
        var categories = AllCategoryNames().ToHashSet(StringComparer.Ordinal);

        CollectionAssert.Contains(categories.ToArray(), "Governance");
        CollectionAssert.Contains(categories.ToArray(), "Contract");
        CollectionAssert.Contains(categories.ToArray(), "Boundary");
        CollectionAssert.Contains(categories.ToArray(), "StaticBoundary");
        CollectionAssert.Contains(categories.ToArray(), "Receipt");
        CollectionAssert.Contains(categories.ToArray(), "Store");

        foreach (var category in new[] { "Governance", "Contract", "Boundary", "StaticBoundary", "Receipt", "Store" })
        foreach (var forbidden in new[] { "Authorized", "ReleaseReady", "Optional", "Quarantined" })
            Assert.IsFalse(category.Contains(forbidden, StringComparison.OrdinalIgnoreCase), $"G13-added category vocabulary must not imply authority or hiding: {category}");
    }

    private static void AssertHasCategoryFragment(TestClassRecord record, string fragment)
    {
        Assert.IsTrue(record.Categories.Any(category => category.Contains(fragment, StringComparison.Ordinal)), $"{record.RelativePath}::{record.ClassName} must keep a {fragment}-style category.");
    }

    private static bool IsRealDatabaseStoreSmokeCategory(string category)
    {
        return category.Contains("RealDatabase", StringComparison.Ordinal)
            || category.Contains("Store", StringComparison.Ordinal)
            || category.Contains("Smoke", StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> AllCategoryNames()
    {
        return CategoryNameCache.Value;
    }

    private static IReadOnlyList<string> LoadAllCategoryNames()
    {
        return IntegrationSourceFiles()
            .SelectMany(file => CategoryAttributeRegex.Matches(File.ReadAllText(file.AbsolutePath)).Select(match => match.Groups[1].Value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(category => category, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<TestClassRecord> TestClassRecords()
    {
        return TestClassRecordCache.Value;
    }

    private static IReadOnlyList<TestClassRecord> LoadTestClassRecords()
    {
        var records = new List<TestClassRecord>();

        foreach (var file in IntegrationSourceFiles())
        {
            var pendingAttributes = new List<string>();

            foreach (var line in File.ReadLines(file.AbsolutePath))
            {
                var trimmed = line.Trim();

                if (trimmed.StartsWith("[", StringComparison.Ordinal))
                {
                    pendingAttributes.Add(trimmed);
                    continue;
                }

                var classMatch = ClassLineRegex.Match(trimmed);
                if (classMatch.Success && pendingAttributes.Any(attribute => attribute.Contains("[TestClass]", StringComparison.Ordinal)))
                {
                    var attributeBlock = string.Join(Environment.NewLine, pendingAttributes);
                    records.Add(new TestClassRecord(
                        file.RelativePath,
                        classMatch.Groups[1].Value,
                        attributeBlock,
                        CategoryAttributeRegex.Matches(attributeBlock).Select(categoryMatch => categoryMatch.Groups[1].Value).ToArray()));

                    pendingAttributes.Clear();
                    continue;
                }

                if (trimmed.Length > 0 && !trimmed.StartsWith("//", StringComparison.Ordinal))
                    pendingAttributes.Clear();
            }
        }

        return records
            .OrderBy(record => record.RelativePath, StringComparer.Ordinal)
            .ThenBy(record => record.ClassName, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<SourceFile> IntegrationSourceFiles()
    {
        return SourceFileCache.Value;
    }

    private static IReadOnlyList<SourceFile> LoadIntegrationSourceFiles()
    {
        var root = RepositoryRoot();
        var sourceRoots = new[]
        {
            Path.Combine(root, "IronDev.IntegrationTests"),
            Path.Combine(root, "IronDev.IntegrationTests.Api")
        };

        return sourceRoots
            .Where(Directory.Exists)
            .SelectMany(sourceRoot => Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => string.Equals(part, "bin", StringComparison.OrdinalIgnoreCase) || string.Equals(part, "obj", StringComparison.OrdinalIgnoreCase)))
            .Select(path => new SourceFile(path, NormalizePath(Path.GetRelativePath(root, path))))
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        Assert.Fail("Could not locate repository root containing IronDev.slnx.");
        return string.Empty;
    }

    private static string NormalizePath(string path)
    {
        return path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }

    private sealed record SourceFile(string AbsolutePath, string RelativePath);
    private sealed record TestClassRecord(string RelativePath, string ClassName, string AttributeBlock, IReadOnlyList<string> Categories);
    private sealed record IgnoreHit(string RelativePath, int LineNumber, string Line);
}
