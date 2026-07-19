using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class WorkbenchPreviewFoundationTests
{
    [TestMethod]
    public void VersionManifest_MatchesApiConfiguration()
    {
        var root = RepositoryRoot();
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "workbench-version.json")));
        using var defaults = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "IronDev.Api", "appsettings.json")));
        using var localTest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "IronDev.Api", "appsettings.LocalTest.json")));

        var version = manifest.RootElement.GetProperty("version").GetString();
        Assert.AreEqual(1, manifest.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.AreEqual("PR-02A", manifest.RootElement.GetProperty("programmePr").GetString());
        Assert.AreEqual(version, defaults.RootElement.GetProperty("WorkbenchV2").GetProperty("Version").GetString());
        Assert.AreEqual(version, localTest.RootElement.GetProperty("WorkbenchV2").GetProperty("Version").GetString());
        Assert.IsFalse(defaults.RootElement.GetProperty("WorkbenchV2").GetProperty("Enabled").GetBoolean());
        Assert.IsTrue(localTest.RootElement.GetProperty("WorkbenchV2").GetProperty("Enabled").GetBoolean());
    }

    [TestMethod]
    public void PreviewLauncher_KeepsDataAndProcessesScoped()
    {
        var root = RepositoryRoot();
        var launcher = File.ReadAllText(Path.Combine(root, "tools", "localtest", "start-alpha-localtest.ps1"));
        var wrapper = File.ReadAllText(Path.Combine(root, "tools", "localtest", "start-pr-manual-test.ps1"));
        var reset = File.ReadAllText(Path.Combine(root, "tools", "localtest", "reset-localtest-data.ps1"));
        var contract = File.ReadAllText(Path.Combine(root, "tools", "localtest", "localtest-seed-contract.ps1"));

        StringAssert.Contains(wrapper, "[string]$PreviewId");
        StringAssert.Contains(wrapper, "[switch]$UseV1");
        StringAssert.Contains(launcher, "ASPNETCORE_URLS = $ApiBaseUrl");
        StringAssert.Contains(launcher, "Cors__AllowedOrigins__0 = \"http://127.0.0.1:$UiPort\"");
        StringAssert.Contains(launcher, "WorkbenchV2__PreviewId = $PreviewId");
        StringAssert.Contains(launcher, "-PreviewId $PreviewId");
        StringAssert.Contains(reset, "Get-LocalTestSeedContract -PreviewId $PreviewId");
        StringAssert.Contains(contract, "IronDeveloper_Test_$databaseSuffix");
        StringAssert.Contains(contract, "Join-Path ([string]$contract.paths.workspaceRoot) $normalizedPreviewId");
        Assert.IsFalse(launcher.Contains("Stop-RepoLocalTestProcesses", StringComparison.Ordinal));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the IronDev repository root.");
    }
}
