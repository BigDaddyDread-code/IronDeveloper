using IronDev.Core.Provisioning;
using IronDev.Infrastructure.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

/// <summary>
/// DOGFOOD-2 entry criterion, pinned against real temporary git repositories: a clean
/// tree reports Clean, uncommitted changes report Dirty with bounded evidence, and a
/// git failure reports Unknown with the error named — never a guessed Clean. A
/// second-repo dogfood run must not start from a dirty or ambiguous source tree
/// unless the system says exactly why that is allowed.
/// </summary>
[TestClass]
public sealed class ProvisioningWorkTreeProbeTests
{
    [TestMethod]
    public async Task ProbeWorkTree_CleanRepository_ReportsClean()
    {
        var repo = CreateTempGitRepo(out var fixtureRoot);
        if (repo is null)
        {
            Assert.Inconclusive("git is not available in this test environment.");
        }

        try
        {
            var (state, detail) = await ProjectProvisioningReadinessService.ProbeWorkTreeAsync(repo, CancellationToken.None);

            Assert.AreEqual(ProvisioningWorkTreeStates.Clean, state, detail);
        }
        finally
        {
            TryDelete(fixtureRoot);
        }
    }

    [TestMethod]
    public async Task ProbeWorkTree_UncommittedChange_ReportsDirty_WithBoundedEvidence()
    {
        var repo = CreateTempGitRepo(out var fixtureRoot);
        if (repo is null)
        {
            Assert.Inconclusive("git is not available in this test environment.");
        }

        try
        {
            File.WriteAllText(Path.Combine(repo, "uncommitted.txt"), "local change");

            var (state, detail) = await ProjectProvisioningReadinessService.ProbeWorkTreeAsync(repo, CancellationToken.None);

            Assert.AreEqual(ProvisioningWorkTreeStates.Dirty, state);
            StringAssert.Contains(detail, "uncommitted.txt");
            StringAssert.Contains(detail, "1 changed path(s)");
        }
        finally
        {
            TryDelete(fixtureRoot);
        }
    }

    [TestMethod]
    public async Task ProbeWorkTree_NotARepository_ReportsUnknown_NeverGuessesClean()
    {
        var fixtureRoot = Path.Combine(Path.GetTempPath(), $"irondev-worktree-probe-{Guid.NewGuid():N}");
        var plainFolder = Path.Combine(fixtureRoot, "not-a-repo");
        Directory.CreateDirectory(plainFolder);

        try
        {
            var (state, detail) = await ProjectProvisioningReadinessService.ProbeWorkTreeAsync(plainFolder, CancellationToken.None);

            Assert.AreEqual(ProvisioningWorkTreeStates.Unknown, state);
            Assert.IsFalse(string.IsNullOrWhiteSpace(detail), "A git failure must name its error.");
        }
        finally
        {
            TryDelete(fixtureRoot);
        }
    }

    /// <summary>Returns the repo path, or null when git is unavailable. Commits one file so the tree starts clean.</summary>
    private static string? CreateTempGitRepo(out string fixtureRoot)
    {
        fixtureRoot = Path.Combine(Path.GetTempPath(), $"irondev-worktree-probe-{Guid.NewGuid():N}");
        var repo = Path.Combine(fixtureRoot, "repo");
        Directory.CreateDirectory(repo);
        File.WriteAllText(Path.Combine(repo, "readme.md"), "probe fixture");

        if (!RunGit(repo, "init") ||
            !RunGit(repo, "config", "user.email", "probe@irondev.test") ||
            !RunGit(repo, "config", "user.name", "IronDev Probe") ||
            !RunGit(repo, "add", ".") ||
            !RunGit(repo, "commit", "-m", "fixture"))
        {
            return null;
        }

        return repo;
    }

    private static bool RunGit(string repo, params string[] arguments)
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo("git")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-C");
            startInfo.ArgumentList.Add(repo);
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = System.Diagnostics.Process.Start(startInfo);
            process!.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                // git objects are read-only on Windows; clear attributes before delete.
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup; the temp folder is namespaced by GUID.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup.
        }
    }
}
