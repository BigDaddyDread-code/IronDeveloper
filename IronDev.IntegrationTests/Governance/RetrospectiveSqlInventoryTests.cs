using System.Diagnostics;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("SqlInventory")]
public sealed class RetrospectiveSqlInventoryTests
{
    [TestMethod]
    public void SqlInventory_ContainsEveryDatabaseSqlFile()
    {
        var entries = InventoryEntries().Select(entry => entry.GetProperty("path").GetString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var databaseSqlFiles = Directory.GetFiles(Path.Combine(RepositoryRoot(), "Database"), "*.sql", SearchOption.TopDirectoryOnly)
            .Select(path => $"Database/{Path.GetFileName(path)}")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.IsTrue(databaseSqlFiles.Length > 0, "Expected Database/*.sql files to exist.");

        foreach (var sqlFile in databaseSqlFiles)
            Assert.IsTrue(entries.Contains(sqlFile), $"SQL inventory must contain {sqlFile}.");
    }

    [TestMethod]
    public void SqlInventory_ContainsEveryMigrationManifestEntry()
    {
        var entries = InventoryEntries().Select(entry => entry.GetProperty("path").GetString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        using var manifest = JsonDocument.Parse(ReadRepositoryFile("Database", "migrations.json"));
        var migrations = manifest.RootElement.GetProperty("migrations").EnumerateArray().ToArray();

        Assert.IsTrue(migrations.Length >= 2, "Current Block G manifest should contain the durable governance substrate migrations.");

        foreach (var migration in migrations)
        {
            var path = migration.GetProperty("path").GetString();
            Assert.IsTrue(entries.Contains(path), $"Migration manifest path missing from inventory: {path}.");
        }
    }

    [TestMethod]
    public void SqlInventory_RuntimeRequiredEntriesDeclareManifestCoverage()
    {
        foreach (var entry in RequiredRuntimeSchemaEntries())
        {
            var appliedByManifest = entry.GetProperty("appliedByManifest");
            Assert.IsTrue(
                appliedByManifest.ValueKind is JsonValueKind.True or JsonValueKind.False,
                $"Required runtime schema entry {entry.GetProperty("id").GetString()} must declare appliedByManifest.");
        }
    }

    [TestMethod]
    public void SqlInventory_RuntimeRequiredEntriesDeclareVerificationCoverage()
    {
        foreach (var entry in RequiredRuntimeSchemaEntries())
        {
            var verifiedByScript = entry.GetProperty("verifiedByScript");
            Assert.IsTrue(
                verifiedByScript.ValueKind is JsonValueKind.True or JsonValueKind.False,
                $"Required runtime schema entry {entry.GetProperty("id").GetString()} must declare verifiedByScript.");
        }
    }

    [TestMethod]
    public void SqlInventory_DoesNotClassifyUnknownRuntimeSqlAsApplied()
    {
        var manifestPaths = ManifestPaths();
        foreach (var entry in RequiredRuntimeSchemaEntries())
        {
            var id = entry.GetProperty("id").GetString();
            var path = entry.GetProperty("path").GetString()!;
            var ownerArea = entry.GetProperty("ownerArea").GetString();
            var applied = entry.GetProperty("appliedByManifest").GetBoolean();

            Assert.AreNotEqual("unknown", ownerArea, $"Required runtime schema entry must have a known owner: {id}.");

            if (applied)
                Assert.IsTrue(manifestPaths.Contains(path), $"Only current manifest paths may be marked appliedByManifest: {id} / {path}.");
        }
    }

    [TestMethod]
    public void SqlInventory_CheckScriptPasses()
    {
        var root = RepositoryRoot();
        var result = RunPowerShellScript(Path.Combine(root, "Database", "check-sql-inventory.ps1"), "-RepositoryRoot", root);

        Assert.AreEqual(0, result.ExitCode, result.Output);
        StringAssert.Contains(result.Output, "SQL inventory check passed.");
    }

    [TestMethod]
    public void SqlInventory_DocumentsRequiredRetrospectiveSections()
    {
        var backendInventory = ReadRepositoryFile("Docs", "BACKEND_SQL_INVENTORY.md");
        var inlineInventory = ReadRepositoryFile("Docs", "BACKEND_INLINE_SQL_INVENTORY.md");
        var receipt = ReadRepositoryFile("Docs", "receipts", "PR74B_RETRO_SQL_INVENTORY_RECEIPT.md");

        foreach (var expected in new[]
        {
            "## 1. Summary",
            "## 2. Required runtime schema",
            "## 3. Required runtime stored procedures",
            "## 4. Required runtime inline SQL",
            "## 5. Required test SQL",
            "## 6. Local/dev utility SQL",
            "## 7. Legacy or unused SQL",
            "## 8. Future migration candidates",
            "## 9. Known gaps",
            "## 10. Next cleanup candidates"
        })
        {
            StringAssert.Contains(backendInventory, expected);
        }

        foreach (var expected in new[]
        {
            "## 1. Runtime inline SQL",
            "## 2. Test inline SQL",
            "## 3. Migration/helper inline SQL",
            "## 4. Inline SQL allowed/forbidden guidance",
            "## 5. Candidates to move behind stored procedures"
        })
        {
            StringAssert.Contains(inlineInventory, expected);
        }

        foreach (var expected in new[]
        {
            "PR74B Retrospective SQL Inventory Receipt",
            "This PR does not change schema or runtime behavior.",
            "PR74c remains necessary for real DB API smoke proof.",
            "Inventory is not migration. Inventory is not approval. Inventory is not cleanup. Inventory is not execution authority."
        })
        {
            StringAssert.Contains(receipt, expected);
        }
    }

    [TestMethod]
    public void SqlInventory_DocumentsCurrentBlockGManifestCoverageOnly()
    {
        var entries = RequiredRuntimeSchemaEntries().ToArray();
        var manifestApplied = entries.Where(entry => entry.GetProperty("appliedByManifest").GetBoolean()).ToArray();
        var verified = entries.Where(entry => entry.GetProperty("verifiedByScript").GetBoolean()).ToArray();

        var manifestPaths = ManifestPaths().ToArray();

        CollectionAssert.AreEquivalent(
            manifestPaths,
            manifestApplied.Select(entry => entry.GetProperty("path").GetString()).ToArray());
        CollectionAssert.AreEquivalent(
            manifestPaths,
            verified.Select(entry => entry.GetProperty("path").GetString()).ToArray());
    }

    private static IReadOnlyCollection<JsonElement> InventoryEntries()
    {
        using var document = JsonDocument.Parse(ReadRepositoryFile("Database", "sql-inventory.json"));
        return document.RootElement.GetProperty("entries").EnumerateArray().Select(entry => entry.Clone()).ToArray();
    }

    private static IEnumerable<JsonElement> RequiredRuntimeSchemaEntries() =>
        InventoryEntries().Where(entry => entry.GetProperty("bucket").GetString() == "required-runtime-schema");

    private static HashSet<string> ManifestPaths()
    {
        using var manifest = JsonDocument.Parse(ReadRepositoryFile("Database", "migrations.json"));
        return manifest.RootElement.GetProperty("migrations")
            .EnumerateArray()
            .Select(migration => migration.GetProperty("path").GetString()!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static (int ExitCode, string Output) RunPowerShellScript(string scriptPath, params string[] args)
    {
        var arguments = new List<string>
        {
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            scriptPath
        };
        arguments.AddRange(args);

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "powershell",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };

        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        process.Start();
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }

    private static string ReadRepositoryFile(params string[] pathParts) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), Path.Combine(pathParts)));

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
