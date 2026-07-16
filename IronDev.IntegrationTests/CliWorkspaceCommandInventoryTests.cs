using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class CliWorkspaceCommandInventoryTests
{
    private static readonly string[] RequiredColumns =
    [
        "Command",
        "Stage",
        "Reads Evidence",
        "Writes Evidence",
        "Mutates Source Repo",
        "Executes Process",
        "Requires Human Approval",
        "Allowed For Agents",
        "Notes"
    ];

    [TestMethod]
    public void CliWorkspaceCommandInventory_AllWorkspaceCommandsAreDocumented()
    {
        var repositoryRoot = FindRepositoryRoot();
        var actualCommands = ReadWorkspaceCommandsFromCli(repositoryRoot);
        var inventory = ReadWorkspaceCommandInventory(repositoryRoot);

        CollectionAssert.AreEquivalent(actualCommands, inventory.Rows.Select(row => row.Command).ToArray());
        Assert.IsTrue(inventory.Rows.Any(row => row.Command == "workspace failure-package"));
        Assert.IsTrue(inventory.Rows.Any(row => row.Command == "workspace source-report"));
        Assert.IsTrue(inventory.Rows.Any(row => row.Command == "workspace post-apply-validate"));
    }

    [TestMethod]
    public void CliWorkspaceCommandInventory_NoStandaloneCommandIsSourceMutating()
    {
        var inventory = ReadWorkspaceCommandInventory(FindRepositoryRoot());
        var sourceMutatingCommands = inventory.Rows
            .Where(row => row.Columns["Mutates Source Repo"].StartsWith("Yes", StringComparison.OrdinalIgnoreCase))
            .Select(row => row.Command)
            .ToArray();

        Assert.AreEqual(0, sourceMutatingCommands.Length);
    }

    [TestMethod]
    public void CliWorkspaceCommandInventory_RequiredBoundaryColumnsExist()
    {
        var inventory = ReadWorkspaceCommandInventory(FindRepositoryRoot());

        CollectionAssert.IsSubsetOf(RequiredColumns, inventory.Columns);
        foreach (var row in inventory.Rows)
        {
            foreach (var column in RequiredColumns)
            {
                Assert.IsTrue(row.Columns.TryGetValue(column, out var value), $"Missing '{column}' for '{row.Command}'.");
                Assert.IsFalse(string.IsNullOrWhiteSpace(value), $"'{column}' must not be empty for '{row.Command}'.");
            }
        }
    }

    private static string[] ReadWorkspaceCommandsFromCli(string repositoryRoot)
    {
        var cliPath = Path.Combine(repositoryRoot, "tools", "IronDev.Cli", "IronDevCli.cs");
        var source = File.ReadAllText(cliPath);
        return Regex.Matches(
                source,
                "private const string\\s+Workspace\\w+Command\\s*=\\s*\"(?<command>workspace [^\"]+)\";",
                RegexOptions.CultureInvariant)
            .Select(match => match.Groups["command"].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(command => command, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static WorkspaceInventory ReadWorkspaceCommandInventory(string repositoryRoot)
    {
        var inventoryPath = Path.Combine(repositoryRoot, "Docs", "cli", "WORKSPACE_COMMAND_BOUNDARY_INVENTORY.md");
        var lines = File.ReadAllLines(inventoryPath);
        var headerIndex = Array.FindIndex(lines, line => line.StartsWith("| Command | Stage |", StringComparison.Ordinal));
        Assert.IsTrue(headerIndex >= 0, "Workspace command inventory table header was not found.");

        var columns = SplitMarkdownRow(lines[headerIndex]);
        var rows = new List<WorkspaceInventoryRow>();
        foreach (var line in lines.Skip(headerIndex + 2))
        {
            if (!line.StartsWith("| `workspace ", StringComparison.Ordinal))
                break;

            var cells = SplitMarkdownRow(line);
            Assert.AreEqual(columns.Length, cells.Length, $"Inventory row has wrong cell count: {line}");
            var values = columns.Zip(cells).ToDictionary(pair => pair.First, pair => pair.Second, StringComparer.OrdinalIgnoreCase);
            rows.Add(new WorkspaceInventoryRow(values["Command"].Trim('`'), values));
        }

        Assert.IsTrue(rows.Count > 0, "Workspace command inventory must include at least one command row.");
        return new WorkspaceInventory(columns, rows);
    }

    private static string[] SplitMarkdownRow(string line) =>
        line.Trim().Trim('|').Split('|').Select(cell => cell.Trim()).ToArray();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "tools", "IronDev.Cli")) &&
                Directory.Exists(Path.Combine(directory.FullName, "IronDev.Core")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate AIDeveloper repository root.");
    }

    private sealed record WorkspaceInventory(string[] Columns, IReadOnlyList<WorkspaceInventoryRow> Rows);

    private sealed record WorkspaceInventoryRow(string Command, IReadOnlyDictionary<string, string> Columns);
}
