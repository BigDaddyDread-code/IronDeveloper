using Microsoft.VisualStudio.TestTools.UnitTesting;
using IronDev.Core.AgentMemory.Collective;

namespace IronDev.IntegrationTests.Agents;

[TestClass]
public sealed class BackendNamingNormalisationTests
{
    [TestMethod]
    public void BackendNamingNormalisation_InventoryDocumentsRequiredBoundaries()
    {
        var repoRoot = FindRepoRoot();
        var inventoryPath = Path.Combine(repoRoot, "Docs", "BACKEND_NAMING_INVENTORY.md");

        Assert.IsTrue(File.Exists(inventoryPath), "Backend naming inventory must exist.");
        var text = File.ReadAllText(inventoryPath);

        foreach (var expected in new[]
        {
            "No behavior change intended.",
            "No SQL/API/CLI/UI/runtime/persistence/capability changes.",
            "Proposal is not apply.",
            "Candidate is not accepted memory.",
            "Audit is not approval.",
            "Gate is not executor.",
            "Critic is not governance.",
            "Memory safety classification is not approval.",
            "Vector, index, and retrieval surfaces remain lookup-only",
            "Human review remains required for source apply and memory promotion."
        })
        {
            StringAssert.Contains(text, expected);
        }
    }

    [TestMethod]
    public void BackendNamingNormalisation_CollectiveRetrievalUsesMatchVocabulary()
    {
        Assert.AreEqual(nameof(CollectiveMemoryRetrievalMatch), typeof(CollectiveMemoryRetrievalMatch).Name);

        var resultPropertyNames = typeof(CollectiveMemoryRetrievalResult)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        CollectionAssert.Contains(resultPropertyNames, "Matches");
        CollectionAssert.DoesNotContain(resultPropertyNames, "Candidates");

        var matchPropertyNames = typeof(CollectiveMemoryRetrievalMatch)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        CollectionAssert.Contains(matchPropertyNames, "RetrievalMatchId");
        CollectionAssert.DoesNotContain(matchPropertyNames, "RetrievalCandidateId");
    }

    [TestMethod]
    public void BackendNamingNormalisation_RemovesOldCollectiveRetrievalCandidateContractName()
    {
        var repoRoot = FindRepoRoot();
        var productionFiles = EnumerateProductionCSharp(repoRoot).ToArray();

        foreach (var file in productionFiles)
        {
            var text = File.ReadAllText(file);
            Assert.IsFalse(
                text.Contains("CollectiveMemoryRetrievalCandidate", StringComparison.Ordinal),
                $"Old retrieval candidate contract name remains in production file: {Path.GetRelativePath(repoRoot, file)}");
            Assert.IsFalse(
                text.Contains("RetrievalCandidateId", StringComparison.Ordinal),
                $"Old retrieval candidate identifier remains in production file: {Path.GetRelativePath(repoRoot, file)}");
        }
    }

    [TestMethod]
    public void BackendNamingNormalisation_DoesNotIntroduceBackendCapabilitySurfaces()
    {
        var repoRoot = FindRepoRoot();
        var changedAreaFiles = new[]
        {
            Path.Combine(repoRoot, "IronDev.Core", "AgentMemory", "Collective", "CollectiveMemoryRetrievalModels.cs"),
            Path.Combine(repoRoot, "IronDev.Infrastructure", "AgentMemory", "SqlCollectiveMemoryRetrievalService.cs"),
            Path.Combine(repoRoot, "Docs", "BACKEND_NAMING_INVENTORY.md")
        };

        var forbidden = new[]
        {
            "ControllerBase",
            "MapPost",
            "HttpPost",
            "IHostedService",
            "BackgroundService",
            "ProcessStartInfo",
            "File.WriteAllText",
            "SqlConnection",
            "CREATE TABLE",
            "CREATE PROCEDURE",
            "ALTER TABLE",
            "INSERT INTO",
            "UPDATE ",
            "DELETE ",
            "WeaviateClient",
            "PromoteCollectiveMemory",
            "CreatePullRequest",
            "SubmitReview"
        };

        foreach (var file in changedAreaFiles)
        {
            Assert.IsTrue(File.Exists(file), $"Expected naming-normalisation file to exist: {file}");
            var text = File.ReadAllText(file);

            foreach (var token in forbidden)
            {
                Assert.IsFalse(
                    text.Contains(token, StringComparison.OrdinalIgnoreCase),
                    $"Naming-normalisation file includes forbidden capability token '{token}': {Path.GetRelativePath(repoRoot, file)}");
            }
        }
    }

    private static IEnumerable<string> EnumerateProductionCSharp(string repoRoot)
    {
        foreach (var root in new[] { "IronDev.Core", "IronDev.Infrastructure", "IronDev.Api", "tools" })
        {
            var path = Path.Combine(repoRoot, root);
            if (!Directory.Exists(path))
                continue;

            foreach (var file in Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
                    file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                yield return file;
            }
        }
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;

        Assert.IsNotNull(directory, "Could not locate repository root.");
        return directory.FullName;
    }
}
