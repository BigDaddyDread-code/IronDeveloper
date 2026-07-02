using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestCategory("Governance")]
[TestCategory("Contract")]
[TestCategory("Boundary")]
[TestClass]
public sealed partial class SlowQuarantineCategoryContractTests
{
    private static readonly Regex CategoryAttributeRegex = new(@"\[TestCategory\(""([^""]*)""\)\]", RegexOptions.Compiled);
    private static readonly Regex ClassLineRegex = new(@"\bclass\s+([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);
    private static readonly Lazy<IReadOnlyList<SourceFile>> SourceFileCache = new(LoadIntegrationSourceFiles);
    private static readonly Lazy<IReadOnlyList<TestClassRecord>> TestClassRecordCache = new(LoadTestClassRecords);
    private static readonly Lazy<IReadOnlyList<RegisterRow>> RegisterRowCache = new(LoadRegisterRows);

    private static readonly string[] TrackedCategories =
    [
        "Slow",
        "LongRunning",
        "Quarantined",
        "RequiresRealDatabase",
        "RequiresExternalDependency",
        "RequiresLocalTooling",
        "ManualLocal"
    ];

    private static readonly string[] ForbiddenCategoryNames =
    [
        "DoNotRun",
        "Skip",
        "Ignored",
        "OptionalForever",
        "Dead",
        "MaybeLater",
        "KnownBad",
        "FlakyButFine",
        "Disabled",
        "Removed"
    ];

    private static readonly string[] AllowedReasons =
    [
        "Slow",
        "RealDatabase",
        "ExternalDependency",
        "LocalTooling",
        "ManualLocal",
        "Flaky",
        "Failing",
        "EnvironmentSensitive",
        "Timeout",
        "Costly"
    ];

    private static readonly string[] AllowedExecutionLanes =
    [
        "DefaultIntegration",
        "GovernanceBoundary",
        "SqlIntegration",
        "SlowIntegration",
        "ManualLocal",
        "QuarantineLane",
        "SelectionOnlyPendingExecution"
    ];

    [TestMethod]
    public void SlowQuarantineCategories_AreRegistered()
    {
        var rows = RegisterRows().ToDictionary(row => row.Test, StringComparer.Ordinal);

        foreach (var record in TrackedTestClassRecords())
        {
            Assert.IsTrue(rows.TryGetValue(TestKey(record), out var row), $"Tracked test must appear in slow/quarantine register: {TestKey(record)}");

            foreach (var category in record.Categories.Where(IsTrackedCategory))
                Assert.IsTrue(row.Categories.Contains(category, StringComparer.Ordinal), $"Register row for {TestKey(record)} must include category {category}.");
        }
    }

    [TestMethod]
    public void QuarantinedTests_HaveOwnerReasonExitCriteriaAndExecutionLane()
    {
        foreach (var row in RegisterRows())
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(row.Owner), $"Register owner must not be blank for {row.Test}.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(row.Reason), $"Register reason must not be blank for {row.Test}.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(row.ExitCriteria), $"Register exit criteria must not be blank for {row.Test}.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(row.ExecutionLane), $"Register execution lane must not be blank for {row.Test}.");
            Assert.IsTrue(AllowedReasons.Contains(row.Reason, StringComparer.Ordinal), $"Unexpected register reason '{row.Reason}' for {row.Test}.");
            Assert.IsTrue(AllowedExecutionLanes.Contains(row.ExecutionLane, StringComparer.Ordinal), $"Unexpected execution lane '{row.ExecutionLane}' for {row.Test}.");
        }
    }

    [TestMethod]
    public void SlowAndDatabaseTests_AreStillSelectable()
    {
        var categories = AllCategoryNames().ToHashSet(StringComparer.Ordinal);
        var inventory = InventoryText();

        foreach (var category in new[] { "RequiresRealDatabase", "LongRunning", "ManualLocal" })
        {
            CollectionAssert.Contains(categories.ToArray(), category);
            StringAssert.Contains(inventory, $"| `{category}` |");
        }

        StringAssert.Contains(inventory, "`RequiresRealDatabase`: 38 test classes, 402 test methods, 38 files.");
        StringAssert.Contains(inventory, "`LongRunning`: 38 test classes, 402 test methods, 38 files.");
        StringAssert.Contains(inventory, "`ManualLocal`: 1 test class, 1 test method, 1 file.");
    }

    [TestMethod]
    public void QuarantinedTests_AreStillSelectable()
    {
        Assert.IsFalse(AllCategoryNames().Contains("Quarantined", StringComparer.Ordinal), "G14 does not introduce Quarantined without a specific flaky/failing test.");
        StringAssert.Contains(RegisterText(), "Quarantined: not introduced in G14.");
        StringAssert.Contains(ReceiptText(), "Quarantined: not introduced.");
    }

    [TestMethod]
    public void ManualLocalTests_AreExplicitLegacyDebt()
    {
        var ignoreHits = IntegrationSourceFiles()
            .SelectMany(file => File.ReadLines(file.AbsolutePath)
                .Select((line, index) => new IgnoreHit(file.RelativePath, index + 1, line.Trim())))
            .Where(hit => hit.Line.StartsWith("[Ignore", StringComparison.Ordinal))
            .ToArray();
        var manualRecord = TestClassRecords().Single(record => record.ClassName == "ManualIndexingTask");
        var row = RegisterRows().Single(registerRow => registerRow.Test == TestKey(manualRecord));

        Assert.HasCount(1, ignoreHits, "The single existing manual local ignore remains explicit legacy debt.");
        Assert.AreEqual("IronDev.IntegrationTests/ManualIndexingTask.cs", ignoreHits[0].RelativePath);
        Assert.IsTrue(manualRecord.Categories.Contains("ManualLocal", StringComparer.Ordinal));
        Assert.AreEqual("ManualLocal", row.ExecutionLane);
        StringAssert.Contains(row.LastKnownStatus, "Existing ignored legacy debt");
    }

    [TestMethod]
    public void NoSlowOrQuarantinedTestCarriesIgnore()
    {
        foreach (var record in TestClassRecords().Where(record =>
                     record.Categories.Contains("Slow", StringComparer.Ordinal) ||
                     record.Categories.Contains("LongRunning", StringComparer.Ordinal) ||
                     record.Categories.Contains("Quarantined", StringComparer.Ordinal)))
        {
            Assert.IsFalse(record.Body.Contains("[Ignore", StringComparison.Ordinal), $"Slow/long-running/quarantined test must not carry [Ignore]: {TestKey(record)}");
        }
    }

    [TestMethod]
    public void NoForbiddenCategoryNamesWereIntroduced()
    {
        foreach (var category in AllCategoryNames())
        foreach (var forbidden in ForbiddenCategoryNames)
            Assert.IsFalse(category.Contains(forbidden, StringComparison.OrdinalIgnoreCase), $"Forbidden category name '{forbidden}' found in '{category}'.");
    }

    [TestMethod]
    public void ExistingCategoryInventoryMentionsG14Split()
    {
        var inventory = InventoryText();

        foreach (var expected in new[]
        {
            "## G14 Slow / Quarantine Split",
            "new categories added",
            "counts by category",
            "tests moved into explicit slow/quarantine visibility",
            "tests remain in default lanes",
            "selection-only pending execution proof",
            "real execution proof",
            "Selection proof is not execution proof."
        })
        {
            StringAssert.Contains(inventory, expected);
        }
    }

    [TestMethod]
    public void SelectionProofIsNotRecordedAsExecutionProof()
    {
        var register = RegisterText();
        var receipt = ReceiptText();

        foreach (var text in new[] { register, receipt })
        {
            StringAssert.Contains(text, "Selection proof means a filter lists tests.");
            StringAssert.Contains(text, "Execution proof means the selected tests ran and passed.");
            StringAssert.Contains(text, "This PR does not treat selection proof as execution proof.");
        }

        StringAssert.Contains(register, "SelectionOnlyPendingExecution");
        StringAssert.Contains(receipt, "## Execution gaps");
    }

    [TestMethod]
    public void Receipt_DocumentsNoDeletionNoIgnoreNoCiWeakening()
    {
        var receipt = ReceiptText();

        foreach (var expected in new[]
        {
            "No tests were deleted.",
            "No [Ignore] attributes were added.",
            "No test assertions changed.",
            "No production behavior changed.",
            "No CI lane was weakened without an explicit replacement lane.",
            "No selection proof was treated as execution proof.",
            "Quarantine is not deletion.",
            "A slow test in a new bucket is still a slow test."
        })
        {
            StringAssert.Contains(receipt, expected);
        }
    }

    private static IReadOnlyList<TestClassRecord> TrackedTestClassRecords()
    {
        return TestClassRecords()
            .Where(record => record.Categories.Any(IsTrackedCategory))
            .OrderBy(record => record.RelativePath, StringComparer.Ordinal)
            .ThenBy(record => record.ClassName, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsTrackedCategory(string category) =>
        TrackedCategories.Contains(category, StringComparer.Ordinal);

    private static IReadOnlyList<string> AllCategoryNames()
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
        var records = new List<MutableTestClassRecord>();

        foreach (var file in IntegrationSourceFiles())
        {
            var pendingAttributes = new List<string>();
            var lines = File.ReadAllLines(file.AbsolutePath);

            for (var index = 0; index < lines.Length; index++)
            {
                var trimmed = lines[index].Trim();

                if (trimmed.StartsWith("[", StringComparison.Ordinal))
                {
                    pendingAttributes.Add(trimmed);
                    continue;
                }

                var classMatch = ClassLineRegex.Match(trimmed);
                if (classMatch.Success && pendingAttributes.Any(attribute => attribute.Contains("[TestClass]", StringComparison.Ordinal)))
                {
                    var attributeBlock = string.Join(Environment.NewLine, pendingAttributes);
                    records.Add(new MutableTestClassRecord(
                        file.RelativePath,
                        classMatch.Groups[1].Value,
                        index,
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
            .GroupBy(record => record.RelativePath, StringComparer.Ordinal)
            .SelectMany(group =>
            {
                var ordered = group.OrderBy(record => record.StartLine).ToArray();
                var path = Path.Combine(RepositoryRoot(), ordered[0].RelativePath.Replace('/', Path.DirectorySeparatorChar));
                var lines = File.ReadAllLines(path);
                var result = new List<TestClassRecord>();

                for (var index = 0; index < ordered.Length; index++)
                {
                    var endLine = index == ordered.Length - 1 ? lines.Length : ordered[index + 1].StartLine;
                    var body = string.Join(Environment.NewLine, lines.Skip(ordered[index].StartLine).Take(endLine - ordered[index].StartLine));
                    result.Add(new TestClassRecord(ordered[index].RelativePath, ordered[index].ClassName, ordered[index].AttributeBlock, ordered[index].Categories, body));
                }

                return result;
            })
            .OrderBy(record => record.RelativePath, StringComparer.Ordinal)
            .ThenBy(record => record.ClassName, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<RegisterRow> RegisterRows()
    {
        return RegisterRowCache.Value;
    }

    private static IReadOnlyList<RegisterRow> LoadRegisterRows()
    {
        return File.ReadLines(RegisterPath())
            .Where(line => line.StartsWith("| ", StringComparison.Ordinal) && !line.Contains("---", StringComparison.Ordinal) && !line.Contains(" Test ", StringComparison.Ordinal))
            .Select(line => line.Trim('|').Split('|').Select(cell => cell.Trim()).ToArray())
            .Select(cells =>
            {
                Assert.AreEqual(9, cells.Length, $"Register row must have 9 columns: {string.Join(" | ", cells)}");
                return new RegisterRow(cells[0], cells[1], cells[2], cells[3], cells[4], cells[5], cells[6], cells[7], cells[8]);
            })
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

    private static string TestKey(TestClassRecord record) =>
        $"{record.RelativePath}::{record.ClassName}";

    private static string InventoryText() =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "testing", "INTEGRATION_TEST_CATEGORIES.md"));

    private static string RegisterText() =>
        File.ReadAllText(RegisterPath());

    private static string ReceiptText() =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "G14_SLOW_TEST_QUARANTINE_CATEGORY_SPLIT.md"));

    private static string RegisterPath() =>
        Path.Combine(RepositoryRoot(), "Docs", "testing", "SLOW_TEST_QUARANTINE_REGISTER.md");

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
    private sealed record MutableTestClassRecord(string RelativePath, string ClassName, int StartLine, string AttributeBlock, IReadOnlyList<string> Categories);
    private sealed record TestClassRecord(string RelativePath, string ClassName, string AttributeBlock, IReadOnlyList<string> Categories, string Body);
    private sealed record IgnoreHit(string RelativePath, int LineNumber, string Line);

    private sealed record RegisterRow(string Test, string Project, string CategoryCell, string Reason, string ExecutionLane, string Owner, string ExitCriteria, string LastKnownStatus, string Notes)
    {
        public IReadOnlyList<string> Categories { get; } = CategoryCell.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }
}
