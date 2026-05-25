using System.IO;
using IronDev.Core.Time;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class DateTimeUtcStandardTests
{
    [TestMethod]
    public void DateTimeDisplay_FormatsUtcMetadataAndTooltip()
    {
        var timestamp = new DateTimeOffset(2026, 5, 25, 2, 32, 0, TimeSpan.Zero);

        Assert.AreEqual("2026-05-25 02:32 UTC", DateTimeDisplay.ToUtcMetadata(timestamp));
        Assert.AreEqual("2026-05-25T02:32:00Z UTC", DateTimeDisplay.ToUtcTooltip(timestamp));
        Assert.IsTrue(DateTimeDisplay.ToLocalDisplay(timestamp).Contains("2026", StringComparison.Ordinal));
        StringAssert.Contains(DateTimeDisplay.ToCompactMetadata(timestamp, "Updated"), "UTC");
    }

    [TestMethod]
    public void DateTimeDisplay_TreatsUnspecifiedDateTimeAsUtc()
    {
        var timestamp = new DateTime(2026, 5, 25, 2, 32, 0, DateTimeKind.Unspecified);

        Assert.AreEqual("2026-05-25 02:32 UTC", DateTimeDisplay.ToUtcMetadata(timestamp));
    }

    [TestMethod]
    public void UtcStandard_DocumentAndGuardExist()
    {
        var root = FindRepositoryRoot();
        var standard = File.ReadAllText(Path.Combine(root, "Docs", "DATETIME_UTC_STANDARD.md"));
        var architecture = File.ReadAllText(Path.Combine(root, "Docs", "ARCHITECTURE.md"));

        StringAssert.Contains(standard, "Persist UTC.");
        StringAssert.Contains(standard, "Transmit UTC.");
        StringAssert.Contains(standard, "Display UTC-aware dates.");
        StringAssert.Contains(standard, "DateTimeDisplay");
        StringAssert.Contains(standard, "Assert-UtcDateTimeStandard.ps1");
        StringAssert.Contains(architecture, "Docs/DATETIME_UTC_STANDARD.md");
        Assert.IsTrue(File.Exists(Path.Combine(root, "Scripts", "Assert-UtcDateTimeStandard.ps1")));
    }

    [TestMethod]
    public void ProductSource_DoesNotUseLocalClockForTimestamps()
    {
        var root = FindRepositoryRoot();
        var forbiddenLocalClock = new[]
        {
            "DateTime" + ".Now",
            "DateTimeOffset" + ".Now"
        };

        foreach (var relativeRoot in new[] { "IronDev.Api", "IronDev.Client", "IronDev.Core", "IronDev.Infrastructure", "IronDeveloper", "tools" })
        {
            var sourceRoot = Path.Combine(root, relativeRoot);
            if (!Directory.Exists(sourceRoot))
                continue;

            foreach (var file in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
                         .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                                        !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)))
            {
                var text = File.ReadAllText(file);
                foreach (var pattern in forbiddenLocalClock)
                {
                    Assert.IsFalse(
                        text.Contains(pattern, StringComparison.Ordinal),
                        $"Product source must not use local timestamp source '{pattern}': {Path.GetRelativePath(root, file)}");
                }
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "IronDeveloper")) &&
                Directory.Exists(Path.Combine(directory.FullName, "IronDev.Core")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate AIDeveloper repository root.");
    }
}
