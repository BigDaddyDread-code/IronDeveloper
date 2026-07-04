using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

/// <summary>
/// D-1 — the demo target is executable proof, not theatre. These tests actually
/// shell out to <c>dotnet build</c>/<c>dotnet test</c> on Samples/BookSeller, so
/// they are genuinely slow and carry the LongRunning category (registered in
/// Docs/testing/SLOW_TEST_QUARANTINE_REGISTER.md, run on the SQL integration
/// lane). The static, fast half lives in AlphaSampleFixtureBoundaryTests.
///
/// Why this matters: the sample is the loop's workspace baseline. If it stops
/// compiling, or its own tests go red, every governed run against it is poisoned
/// from the start — so a clean build/test of the sample is a build-path
/// guarantee, not a nicety.
/// </summary>
[TestClass]
[TestCategory("LongRunning")]
public sealed class AlphaSampleBuildSmokeTests
{
    [TestMethod]
    public void Sample_CompilesGreen_AsIs()
    {
        var sampleRoot = Path.Combine(RepoRoot(), "Samples", "BookSeller");
        var (exitCode, output) = RunDotnet("build BookSeller.slnx", sampleRoot);

        Assert.AreEqual(0, exitCode,
            $"Samples/BookSeller must compile as-is — a demo that does not build is theatre.{Environment.NewLine}{Tail(output)}");
    }

    [TestMethod]
    public void SampleTests_PassGreen_AsIs()
    {
        var sampleRoot = Path.Combine(RepoRoot(), "Samples", "BookSeller");
        var (exitCode, output) = RunDotnet("test BookSeller.slnx", sampleRoot);

        Assert.AreEqual(0, exitCode,
            $"The sample's own tests must pass as-is — the loop's workspace runs them, so a red baseline poisons every run.{Environment.NewLine}{Tail(output)}");
    }

    private static (int ExitCode, string Output) RunDotnet(string arguments, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo("dotnet", arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        Assert.IsTrue(process.WaitForExit(TimeSpan.FromMinutes(5)), $"dotnet {arguments} timed out.");
        return (process.ExitCode, stdout + stderr);
    }

    private static string Tail(string output)
    {
        var lines = output.Split('\n');
        return string.Join('\n', lines.Skip(Math.Max(0, lines.Length - 25)));
    }

    private static string RepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "IronDev.slnx")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
