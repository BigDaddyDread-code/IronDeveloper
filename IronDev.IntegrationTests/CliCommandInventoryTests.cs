using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class CliCommandInventoryTests
{
    [TestMethod]
    public void CliCommandInventory_ShouldBeFlatUniqueAndSorted()
    {
        var inventoryPath = Path.Combine(FindRepositoryRoot(), "tools", "dogfood", "cli-command-inventory.json");
        using var document = JsonDocument.Parse(File.ReadAllText(inventoryPath));
        var commands = document.RootElement.EnumerateArray()
            .Select(item => new CliCommandInventoryItem(
                ReadRequiredString(item, "category"),
                ReadRequiredString(item, "command")))
            .ToArray();

        Assert.IsTrue(commands.Length > 0);
        CollectionAssert.AreEqual(
            commands.Select(item => item.Command).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            commands.Select(item => item.Command).ToArray());
        CollectionAssert.AreEqual(
            commands
                .OrderBy(item => item.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Command, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            commands);
    }

    private static string ReadRequiredString(JsonElement root, string propertyName)
    {
        Assert.IsTrue(root.TryGetProperty(propertyName, out var property), $"Missing '{propertyName}'.");
        Assert.AreEqual(JsonValueKind.String, property.ValueKind, $"'{propertyName}' must be a string.");
        var value = property.GetString();
        Assert.IsFalse(string.IsNullOrWhiteSpace(value), $"'{propertyName}' must not be empty.");
        return value;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "tools", "dogfood")) &&
                Directory.Exists(Path.Combine(directory.FullName, "IronDev.Core")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate AIDeveloper repository root.");
    }

    private sealed record CliCommandInventoryItem(string Category, string Command);
}
