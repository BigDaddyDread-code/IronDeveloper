using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Agents;

[TestClass]
public sealed class BackendDeadCodeSweepTests
{
    [TestMethod]
    public void BackendDeadCodeSweep_RemovesOrphanedManualLoopIssueCodesAndDtos()
    {
        var repoRoot = FindRepoRoot();
        var files = ReadProductionCSharp(repoRoot);
        var combined = string.Join(Environment.NewLine, files.Select(file => file.Text));

        Assert.IsFalse(combined.Contains("ManualDogfoodHarnessStageResult", StringComparison.Ordinal));
        Assert.IsFalse(combined.Contains("DOGFOOD_HARNESS_RUNTIME_WIRING_FORBIDDEN", StringComparison.Ordinal));
        Assert.IsFalse(combined.Contains("REAL_RUN_MEMORY_RUNTIME_WIRING_FORBIDDEN", StringComparison.Ordinal));
        Assert.IsFalse(combined.Contains("TEST_FAILURE_LOOP_RUNTIME_WIRING_FORBIDDEN", StringComparison.Ordinal));
        Assert.IsFalse(combined.Contains("CMEM_STABILITY_MEMORY_INVALID", StringComparison.Ordinal));
    }

    [TestMethod]
    public void BackendDeadCodeSweep_DoesNotWireManualDogfoodOrManualLoopServicesIntoRuntimeSurfaces()
    {
        var repoRoot = FindRepoRoot();
        var runtimeFiles = ReadProductionCSharp(repoRoot)
            .Where(file =>
                file.RelativePath.StartsWith("IronDev.Api", StringComparison.OrdinalIgnoreCase) ||
                file.RelativePath.StartsWith("tools", StringComparison.OrdinalIgnoreCase) ||
                file.RelativePath.StartsWith("IronDev.Infrastructure", StringComparison.OrdinalIgnoreCase) ||
                file.RelativePath.StartsWith("IronDev.Client", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var forbidden = new[]
        {
            "ManualDogfoodHarnessService",
            "IManualDogfoodHarnessService",
            "ManualRealRunMemoryImprovementService",
            "ManualTestFailureRepairProposalLoopService",
            "ManualTicketReviewFixProposalLoopService",
            "ManualImplementationAgentPatchProposalService",
            "ManualTesterAgentToolExecutionService"
        };

        foreach (var file in runtimeFiles)
        {
            foreach (var token in forbidden)
            {
                Assert.IsFalse(
                    file.Text.Contains(token, StringComparison.Ordinal),
                    $"Runtime/API/CLI file references manual-only service '{token}': {file.RelativePath}");
            }
        }
    }

    [TestMethod]
    public void BackendDeadCodeSweep_DoesNotIntroduceNewRuntimeCapabilityTokens()
    {
        var repoRoot = FindRepoRoot();
        var changedArea = ReadProductionCSharp(repoRoot)
            .Where(file =>
                file.RelativePath.Contains("ManualDogfoodHarnessService.cs", StringComparison.OrdinalIgnoreCase) ||
                file.RelativePath.Contains("ManualRealRunMemoryImprovementService.cs", StringComparison.OrdinalIgnoreCase) ||
                file.RelativePath.Contains("ManualTestFailureRepairProposalLoopService.cs", StringComparison.OrdinalIgnoreCase) ||
                file.RelativePath.Contains("CollectiveMemoryStabilityScorer.cs", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var forbidden = new[]
        {
            "AgentToolRouter",
            "AgentRuntime",
            "AgentScheduler",
            "AgentOrchestrator",
            "BackgroundService",
            "IHostedService",
            "AddHostedService",
            "ProcessStartInfo",
            "SqlConnection",
            "WeaviateClient",
            "SubmitReview"
        };

        foreach (var file in changedArea)
        {
            var normalised = file.Text
                .Replace("PromoteCollectiveMemory", string.Empty, StringComparison.Ordinal)
                .Replace("PromotesMemory", string.Empty, StringComparison.Ordinal)
                .Replace("WritesWeaviate", string.Empty, StringComparison.Ordinal)
                .Replace("CreatePullRequest", string.Empty, StringComparison.Ordinal)
                .Replace("CreatesPullRequest", string.Empty, StringComparison.Ordinal);

            foreach (var token in forbidden)
            {
                Assert.IsFalse(
                    normalised.Contains(token, StringComparison.Ordinal),
                    $"Cleanup-touched file includes forbidden runtime token '{token}': {file.RelativePath}");
            }
        }
    }

    private static IReadOnlyList<(string RelativePath, string Text)> ReadProductionCSharp(string repoRoot)
    {
        var root = new DirectoryInfo(repoRoot);
        return Directory.EnumerateFiles(repoRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}IronDev.IntegrationTests{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Select(path => (
                Path.GetRelativePath(root.FullName, path),
                File.ReadAllText(path)))
            .ToArray();
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
