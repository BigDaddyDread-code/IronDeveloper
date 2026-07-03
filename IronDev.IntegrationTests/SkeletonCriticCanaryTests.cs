using IronDev.Core.Builder;
using IronDev.Infrastructure.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

/// <summary>
/// P1-5 — the canary corpus. These tests are the corpus's own proof: every
/// seeded defect must be caught by the REAL critic path while the model is
/// maximally agreeable, and the honest control must come back clean. A canary
/// that stops being caught is a hole in the net — and this suite is what makes
/// that hole loud.
/// </summary>
[TestClass]
[TestCategory("SkeletonRun")]
public sealed class SkeletonCriticCanaryTests
{
    [TestMethod]
    public void Catalog_EveryCanaryStatesItsDefectAndWhatMustBeCaught()
    {
        var canaries = SkeletonCriticCanaryCatalog.All;

        Assert.IsTrue(canaries.Count(canary => !canary.IsControl) >= 5,
            "The corpus covers at least the five seeded defect classes from the Phase 1 plan.");
        Assert.AreEqual(1, canaries.Count(canary => canary.IsControl),
            "Exactly one honest control keeps the corpus honest the other way.");
        Assert.AreEqual(canaries.Count, canaries.Select(canary => canary.CanaryId).Distinct(StringComparer.Ordinal).Count(),
            "Canary ids are unique — results must be attributable.");

        foreach (var canary in canaries)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(canary.SeededDefect), $"{canary.CanaryId} must state its seeded defect.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(canary.MustCatch), $"{canary.CanaryId} must state what a competent critic catches.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(canary.MinimumVerdict), $"{canary.CanaryId} must state its minimum verdict.");
        }
    }

    [TestMethod]
    public void Catalog_CoversTheFiveSeededDefectClasses()
    {
        var expectedByClass = new Dictionary<string, string>
        {
            ["canary-package-tamper"] = SkeletonGroundTruthCheckNames.PackageHash,
            ["canary-self-contradiction"] = SkeletonGroundTruthCheckNames.InternalConsistency,
            ["canary-phantom-receipt"] = SkeletonGroundTruthCheckNames.CommandEvidence,
            ["canary-forged-coverage"] = SkeletonGroundTruthCheckNames.CriterionCoverage,
            ["canary-green-lie"] = SkeletonGroundTruthCheckNames.ReExecution
        };

        foreach (var (canaryId, expectedCheck) in expectedByClass)
        {
            var canary = SkeletonCriticCanaryCatalog.All.Single(candidate => candidate.CanaryId == canaryId);
            CollectionAssert.Contains(canary.ExpectedFailedChecks.ToList(), expectedCheck,
                $"{canaryId} targets the {expectedCheck} tension.");
        }
    }

    [TestMethod]
    public async Task Corpus_EveryCanaryIsCaught_AndTheControlComesBackClean_DespiteAnAgreeableModel()
    {
        using var repo = TempCanarySandbox.Create();
        var runner = new SkeletonCriticCanaryRunner();

        var corpus = await runner.RunAsync(new SkeletonCanaryRunOptions { SandboxRepoPath = repo.Path });

        foreach (var result in corpus.Results)
        {
            Console.WriteLine($"[{(result.Caught ? "CAUGHT" : "MISSED")}] {result.CanaryId}: expected {result.Expected}; observed {result.Observed}");
        }

        foreach (var result in corpus.Results.Where(result => !result.IsControl))
        {
            Assert.IsTrue(result.Caught,
                $"MISSED CANARY {result.CanaryId} — {result.MustCatch} Observed: {result.Observed}. " +
                "A canary the critic misses is a hole in the net, and the net is why anyone stops watching.");
        }

        Assert.IsTrue(corpus.ControlClean,
            $"The honest control was flagged: {corpus.Results.Single(result => result.IsControl).Observed}. " +
            "A net that flags everything catches nothing.");
        Assert.AreEqual(1.0, corpus.CatchRate, 0.000001, "Every seeded defect class is caught: catch-rate 5/5.");
        StringAssert.Contains(corpus.Boundary, "grants nothing");
    }

    [TestMethod]
    public async Task Corpus_WithoutASandbox_TheGreenLieGoesHonestlyUncaught_NeverSilentlyPassed()
    {
        // No sandbox → re-execution reports itself unavailable. The green-lie
        // canary is then MISSED — and the corpus says so, because pretending to
        // verify is the exact failure mode this whole phase exists to prevent.
        var runner = new SkeletonCriticCanaryRunner();

        var corpus = await runner.RunAsync(new SkeletonCanaryRunOptions { SandboxRepoPath = null });

        var greenLie = corpus.Results.Single(result => result.CanaryId == "canary-green-lie");
        Assert.IsFalse(greenLie.Caught, "Without re-execution the green lie cannot be caught, and the corpus must not pretend otherwise.");
        Assert.IsTrue(corpus.CatchRate < 1.0, "The catch-rate drops when verification capability degrades — the number is honest.");

        var tamper = corpus.Results.Single(result => result.CanaryId == "canary-package-tamper");
        Assert.IsTrue(tamper.Caught, "Checks that need no sandbox still catch their canaries.");
    }

    private sealed class TempCanarySandbox : IDisposable
    {
        public required string Path { get; init; }

        public static TempCanarySandbox Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "irondev-canary-repo-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            RunTool(path, "git", "init");
            RunTool(path, "git", "config user.email canary@irondev.local");
            RunTool(path, "git", "config user.name Canary");
            File.WriteAllText(System.IO.Path.Combine(path, "Directory.Build.props"), """
                <Project>
                  <PropertyGroup>
                    <MSBuildProjectExtensionsPath>.assets/</MSBuildProjectExtensionsPath>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(System.IO.Path.Combine(path, "Sandbox.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                  </PropertyGroup>
                </Project>
                """);
            Directory.CreateDirectory(System.IO.Path.Combine(path, "src"));
            File.WriteAllText(System.IO.Path.Combine(path, "src", "Existing.cs"), "namespace Sandbox; public static class Existing { }");
            RunTool(path, "dotnet", "restore");
            RunTool(path, "git", "add .");
            RunTool(path, "git", "commit -m initial");
            return new TempCanarySandbox { Path = path };
        }

        private static void RunTool(string workingDirectory, string fileName, string arguments)
        {
            var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(fileName, arguments)
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            })!;
            process.WaitForExit();
            Assert.AreEqual(0, process.ExitCode, $"{fileName} {arguments} failed: {process.StandardError.ReadToEnd()}{process.StandardOutput.ReadToEnd()}");
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    foreach (var file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
                        File.SetAttributes(file, FileAttributes.Normal);
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
                // best-effort cleanup of temp repos
            }
        }
    }
}
