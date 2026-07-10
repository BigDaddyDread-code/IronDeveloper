namespace IronDev.IntegrationTests;

[TestClass]
public sealed class LocalTestChatDocumentContextSchemaTests
{
    [TestMethod]
    public void LocalTestReset_AppliesChatDocumentContextMigrationBeforeSeeding()
    {
        var script = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "tools",
            "localtest",
            "reset-localtest-data.ps1"));

        var migrationIndex = script.IndexOf(
            "Database\\migrate_chat_document_sources.sql",
            StringComparison.Ordinal);
        var seedIndex = script.IndexOf(
            "localtest-seed.sql",
            StringComparison.Ordinal);

        Assert.IsTrue(migrationIndex >= 0, "LocalTest reset must apply the Chat document-context migration.");
        Assert.IsTrue(seedIndex >= 0, "LocalTest reset must still apply its deterministic seed.");
        Assert.IsTrue(migrationIndex < seedIndex, "The Chat document-context schema must exist before LocalTest seed data is loaded.");
        StringAssert.Contains(script, "\"-b\", \"-I\", \"-i\", $Path");
    }

    [TestMethod]
    public void LocalTestScripts_ResolveTheLatestPipeAnnouncementWithoutATailWindow()
    {
        foreach (var fileName in new[]
        {
            "reset-localtest-data.ps1",
            "start-alpha-localtest.ps1",
            "Invoke-LocalTestSmoke.ps1"
        })
        {
            var script = File.ReadAllText(Path.Combine(RepositoryRoot(), "tools", "localtest", fileName));

            StringAssert.Contains(script, "Select-String -LiteralPath $errorLog");
            StringAssert.Contains(script, "Select-Object -Last 1");
            Assert.IsFalse(
                script.Contains("Get-Content -LiteralPath $errorLog | Select-Object -Last 200", StringComparison.Ordinal),
                $"{fileName} must not lose the LocalDB pipe announcement when the error log grows.");
        }
    }

    [TestMethod]
    public void ChatDocumentContextMigration_DeclaresFilteredIndexSessionSettings()
    {
        var migration = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "Database",
            "migrate_chat_document_sources.sql"));

        StringAssert.Contains(migration, "SET ANSI_NULLS ON;");
        StringAssert.Contains(migration, "SET QUOTED_IDENTIFIER ON;");
        StringAssert.Contains(migration, "WHERE ReplyToMessageId IS NOT NULL;");
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

        throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
