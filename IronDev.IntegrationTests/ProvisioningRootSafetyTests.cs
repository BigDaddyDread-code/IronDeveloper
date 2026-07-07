using IronDev.Infrastructure.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

/// <summary>
/// PROJECT-1 root safety, pinned. The provisioning screen decides whether a folder is
/// safe to treat as a repository root — a wizard that calls a path safe becomes the
/// front door to mutation, so root safety cannot be "mostly" safe. These tests pin the
/// refusals: drive roots, the user-profile root, system directories, relative and
/// traversal paths, and — critically — paths that are or live under a symlink/reparse
/// point, because "looks under a safe folder" can silently point somewhere else.
/// Mirrors the LocalRootSafetyValidator reparse semantics proven by BlockJ10.
/// </summary>
[TestClass]
public sealed class ProvisioningRootSafetyTests
{
    [TestMethod]
    public void ProvisioningRootSafety_RejectsDriveRoot()
    {
        var driveRoot = Path.GetPathRoot(Path.GetTempPath())!;

        var (isSafe, detail) = ProjectProvisioningReadinessService.CheckRootSafety(driveRoot);

        Assert.IsFalse(isSafe);
        StringAssert.Contains(detail, "drive root");
    }

    [TestMethod]
    public void ProvisioningRootSafety_RejectsUserProfileRoot()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.IsFalse(string.IsNullOrWhiteSpace(userProfile), "Test environment must expose a user profile.");

        var (isSafe, detail) = ProjectProvisioningReadinessService.CheckRootSafety(userProfile);

        Assert.IsFalse(isSafe);
        StringAssert.Contains(detail, "protected system or user-profile root");
    }

    [TestMethod]
    public void ProvisioningRootSafety_RejectsSystemDirectory()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        Assert.IsFalse(string.IsNullOrWhiteSpace(windows), "Test environment must expose the Windows directory.");

        var rootItself = ProjectProvisioningReadinessService.CheckRootSafety(windows);
        Assert.IsFalse(rootItself.IsSafe);

        var childOfSystem = ProjectProvisioningReadinessService.CheckRootSafety(Path.Combine(windows, "System32"));
        Assert.IsFalse(childOfSystem.IsSafe);
        StringAssert.Contains(childOfSystem.Detail, "protected system directory");
    }

    [TestMethod]
    public void ProvisioningRootSafety_RejectsReparsePointAncestor()
    {
        var fixtureRoot = Path.Combine(Path.GetTempPath(), $"irondev-prov-root-safety-{Guid.NewGuid():N}");
        var target = Path.Combine(fixtureRoot, "junction-target");
        var junction = Path.Combine(fixtureRoot, "junction-repo");
        Directory.CreateDirectory(target);

        if (!TryCreateDirectoryJunction(junction, target))
        {
            Assert.Inconclusive("Directory junction creation is not available in this test environment.");
        }

        try
        {
            // The junction itself must refuse — it IS a reparse point.
            var junctionResult = ProjectProvisioningReadinessService.CheckRootSafety(junction);
            Assert.IsFalse(junctionResult.IsSafe);
            StringAssert.Contains(junctionResult.Detail, "reparse point");

            // A child under the junction must refuse — its ancestor chain lies about where it lives.
            var childResult = ProjectProvisioningReadinessService.CheckRootSafety(Path.Combine(junction, "src"));
            Assert.IsFalse(childResult.IsSafe);
            StringAssert.Contains(childResult.Detail, "reparse point");
        }
        finally
        {
            TryRemoveDirectoryJunction(junction);
            try
            {
                Directory.Delete(fixtureRoot, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup; the temp folder is namespaced by GUID.
            }
        }
    }

    [TestMethod]
    public void ProvisioningRootSafety_RejectsRelativeAndTraversalPaths()
    {
        Assert.IsFalse(ProjectProvisioningReadinessService.CheckRootSafety("relative-repo").IsSafe);
        Assert.IsFalse(
            ProjectProvisioningReadinessService.CheckRootSafety(Path.Combine(Path.GetTempPath(), "..", "escape")).IsSafe);
    }

    [TestMethod]
    public void ProvisioningRootSafety_AcceptsDedicatedRepositoryFolder()
    {
        var fixtureRoot = Path.Combine(Path.GetTempPath(), $"irondev-prov-safe-{Guid.NewGuid():N}");
        var repo = Path.Combine(fixtureRoot, "repos", "second");
        Directory.CreateDirectory(repo);
        try
        {
            var (isSafe, detail) = ProjectProvisioningReadinessService.CheckRootSafety(repo);
            Assert.IsTrue(isSafe, detail);
        }
        finally
        {
            Directory.Delete(fixtureRoot, recursive: true);
        }
    }

    private static bool TryCreateDirectoryJunction(string junctionPath, string targetPath)
    {
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            Arguments = $"/c mklink /J \"{junctionPath}\" \"{targetPath}\""
        });
        process!.WaitForExit();
        return process.ExitCode == 0 && Directory.Exists(junctionPath);
    }

    private static void TryRemoveDirectoryJunction(string junctionPath)
    {
        try
        {
            if (Directory.Exists(junctionPath))
            {
                Directory.Delete(junctionPath, recursive: false);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
    }
}
