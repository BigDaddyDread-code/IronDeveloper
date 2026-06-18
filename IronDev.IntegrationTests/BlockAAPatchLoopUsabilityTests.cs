using System.Diagnostics;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockAAPatchLoopUsabilityTests
{
    [TestMethod]
    public async Task BlockAA_FinishBlocksForbiddenFilesAndWritesScopeArtifacts()
    {
        var root = CreateTempRoot();
        try
        {
            var repo = await CreateSourceRepoAsync(root, includeProfiles: false);
            var runsRoot = Path.Combine(root, "runs");
            var workspaceRoot = Path.Combine(root, "workspaces");

            var start = await RunCliAsync(
                "patch", "start",
                "--repo", repo,
                "--task", Path.Combine(root, "task.md"),
                "--test", "dotnet --version",
                "--allow", "README.md",
                "--forbid", "secrets/**",
                "--runs-root", runsRoot,
                "--workspace-root", workspaceRoot,
                "--run-id", "aa-scope",
                "--json");

            Assert.AreEqual(0, start.ExitCode, start.StandardError);

            var runPath = Path.Combine(runsRoot, "aa-scope");
            var workspace = ReadRunString(runPath, "workspacePath");
            await File.AppendAllTextAsync(Path.Combine(workspace, "README.md"), "safe change\n");
            Directory.CreateDirectory(Path.Combine(workspace, "secrets"));
            await File.WriteAllTextAsync(Path.Combine(workspace, "secrets", "api.key"), "not-a-real-secret\n");

            var finish = await RunCliAsync(
                "patch", "finish",
                "--run", "aa-scope",
                "--runs-root", runsRoot,
                "--skip-test",
                "--json");

            Assert.AreNotEqual(0, finish.ExitCode, finish.StandardOutput + finish.StandardError);
            StringAssert.Contains(finish.StandardOutput, "blocked");

            var scope = await File.ReadAllTextAsync(Path.Combine(runPath, "file-scope-result.md"));
            StringAssert.Contains(scope.Replace('\\', '/'), "secrets/api.key");

            var risk = await File.ReadAllTextAsync(Path.Combine(runPath, "patch-risk-summary.md"));
            StringAssert.Contains(risk.ToLowerInvariant(), "forbidden");

            Assert.IsTrue(ReadRunInt(runPath, "changedFileCount") >= 2);
            Assert.IsTrue(ReadRunInt(runPath, "blockedFileCount") >= 1);
            Assert.IsFalse(File.Exists(Path.Combine(repo, "secrets", "api.key")));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [TestMethod]
    public async Task BlockAA_SourceSnapshotRecordsDirtyAndHeadChangedWarnings()
    {
        var root = CreateTempRoot();
        try
        {
            var repo = await CreateSourceRepoAsync(root, includeProfiles: false);
            var runsRoot = Path.Combine(root, "runs");
            var workspaceRoot = Path.Combine(root, "workspaces");

            await File.WriteAllTextAsync(Path.Combine(repo, "dirty-before-start.txt"), "local scratch\n");

            var start = await RunCliAsync(
                "patch", "start",
                "--repo", repo,
                "--task", Path.Combine(root, "task.md"),
                "--test", "dotnet --version",
                "--runs-root", runsRoot,
                "--workspace-root", workspaceRoot,
                "--run-id", "aa-snapshot",
                "--json");

            Assert.AreEqual(0, start.ExitCode, start.StandardError);

            await File.WriteAllTextAsync(Path.Combine(repo, "source-head-after-start.txt"), "new source commit\n");
            await GitAsync(repo, "add", "source-head-after-start.txt");
            await GitAsync(repo, "commit", "-m", "advance source head");

            var runPath = Path.Combine(runsRoot, "aa-snapshot");
            var workspace = ReadRunString(runPath, "workspacePath");
            await File.AppendAllTextAsync(Path.Combine(workspace, "README.md"), "workspace patch\n");

            var finish = await RunCliAsync(
                "patch", "finish",
                "--run", "aa-snapshot",
                "--runs-root", runsRoot,
                "--skip-test",
                "--json");

            Assert.AreEqual(0, finish.ExitCode, finish.StandardOutput + finish.StandardError);
            Assert.IsTrue(ReadRunBool(runPath, "sourceRepoDirtyAtStart"));
            Assert.IsTrue(ReadRunBool(runPath, "sourceHeadChangedSinceStart"));

            var review = await File.ReadAllTextAsync(Path.Combine(runPath, "review-summary.md"));
            StringAssert.Contains(review, "Source HEAD changed since run start");

            var risks = await File.ReadAllTextAsync(Path.Combine(runPath, "known-risks.md"));
            StringAssert.Contains(risks.ToLowerInvariant(), "uncommitted");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [TestMethod]
    public async Task BlockAA_TestProfilesRerunListStatusAndCleanupStayInspectionOnly()
    {
        var root = CreateTempRoot();
        try
        {
            var repo = await CreateSourceRepoAsync(root, includeProfiles: true);
            var runsRoot = Path.Combine(root, "runs");
            var workspaceRoot = Path.Combine(root, "workspaces");

            var start = await RunCliAsync(
                "patch", "start",
                "--repo", repo,
                "--task", Path.Combine(root, "task.md"),
                "--test-profile", "quick",
                "--runs-root", runsRoot,
                "--workspace-root", workspaceRoot,
                "--run-id", "aa-profile",
                "--json");

            Assert.AreEqual(0, start.ExitCode, start.StandardOutput + start.StandardError);

            var runPath = Path.Combine(runsRoot, "aa-profile");
            var workspace = ReadRunString(runPath, "workspacePath");
            await File.AppendAllTextAsync(Path.Combine(workspace, "README.md"), "profile change\n");

            var test = await RunCliAsync("patch", "test", "--run", "aa-profile", "--runs-root", runsRoot, "--json");
            Assert.AreEqual(0, test.ExitCode, test.StandardOutput + test.StandardError);
            Assert.AreEqual("quick", ReadRunString(runPath, "testProfileName"));
            Assert.AreEqual("Passed", ReadRunString(runPath, "testStatus"));

            var status = await RunCliAsync("patch", "status", "--run", "aa-profile", "--runs-root", runsRoot, "--json");
            Assert.AreEqual(0, status.ExitCode, status.StandardOutput + status.StandardError);
            StringAssert.Contains(status.StandardOutput, "aa-profile");

            var list = await RunCliAsync("patch", "list", "--runs-root", runsRoot, "--json");
            Assert.AreEqual(0, list.ExitCode, list.StandardOutput + list.StandardError);
            StringAssert.Contains(list.StandardOutput, "aa-profile");

            var finish = await RunCliAsync("patch", "finish", "--run", "aa-profile", "--runs-root", runsRoot, "--skip-test", "--json");
            Assert.AreEqual(0, finish.ExitCode, finish.StandardOutput + finish.StandardError);

            var cleanupMissingIntent = await RunCliAsync("patch", "cleanup", "--run", "aa-profile", "--runs-root", runsRoot, "--json");
            Assert.AreNotEqual(0, cleanupMissingIntent.ExitCode, cleanupMissingIntent.StandardOutput + cleanupMissingIntent.StandardError);

            var cleanup = await RunCliAsync("patch", "cleanup", "--run", "aa-profile", "--runs-root", runsRoot, "--delete-workspace", "--json");
            Assert.AreEqual(0, cleanup.ExitCode, cleanup.StandardOutput + cleanup.StandardError);
            Assert.IsFalse(Directory.Exists(workspace));
            Assert.IsTrue(File.Exists(Path.Combine(runPath, "run.json")));
            Assert.AreEqual("WorkspaceDeleted", ReadRunString(runPath, "cleanupStatus"));

            var cleanupSummary = await File.ReadAllTextAsync(Path.Combine(runPath, "cleanup-summary.md"));
            StringAssert.Contains(cleanupSummary, "WorkspaceDeleted");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [TestMethod]
    public async Task BlockAA_FailedTestWritesReadableSummaryAndRisk()
    {
        var root = CreateTempRoot();
        try
        {
            var repo = await CreateSourceRepoAsync(root, includeProfiles: false);
            var runsRoot = Path.Combine(root, "runs");
            var workspaceRoot = Path.Combine(root, "workspaces");

            var start = await RunCliAsync(
                "patch", "start",
                "--repo", repo,
                "--task", Path.Combine(root, "task.md"),
                "--test", "dotnet definitely-not-a-real-command-aa",
                "--runs-root", runsRoot,
                "--workspace-root", workspaceRoot,
                "--run-id", "aa-failing-test",
                "--json");

            Assert.AreEqual(0, start.ExitCode, start.StandardOutput + start.StandardError);

            var runPath = Path.Combine(runsRoot, "aa-failing-test");
            var workspace = ReadRunString(runPath, "workspacePath");
            await File.AppendAllTextAsync(Path.Combine(workspace, "README.md"), "failing test change\n");

            var finish = await RunCliAsync("patch", "finish", "--run", "aa-failing-test", "--runs-root", runsRoot, "--json");
            Assert.AreNotEqual(0, finish.ExitCode, finish.StandardOutput + finish.StandardError);
            Assert.AreEqual("Failed", ReadRunString(runPath, "testStatus"));

            var summary = await File.ReadAllTextAsync(Path.Combine(runPath, "test-output-summary.md"));
            StringAssert.Contains(summary, "dotnet definitely-not-a-real-command-aa");
            StringAssert.Contains(summary.ToLowerInvariant(), "exit code");

            var risk = await File.ReadAllTextAsync(Path.Combine(runPath, "patch-risk-summary.md"));
            StringAssert.Contains(risk.ToLowerInvariant(), "test");
            StringAssert.Contains(risk.ToLowerInvariant(), "failed");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [TestMethod]
    public async Task BlockAA_FinishCanBeRerunAndRefreshesPatchArtifacts()
    {
        var root = CreateTempRoot();
        try
        {
            var repo = await CreateSourceRepoAsync(root, includeProfiles: false);
            var runsRoot = Path.Combine(root, "runs");
            var workspaceRoot = Path.Combine(root, "workspaces");

            var start = await RunCliAsync(
                "patch", "start",
                "--repo", repo,
                "--task", Path.Combine(root, "task.md"),
                "--test", "dotnet --version",
                "--runs-root", runsRoot,
                "--workspace-root", workspaceRoot,
                "--run-id", "aa-rerun",
                "--json");

            Assert.AreEqual(0, start.ExitCode, start.StandardOutput + start.StandardError);

            var runPath = Path.Combine(runsRoot, "aa-rerun");
            var workspace = ReadRunString(runPath, "workspacePath");
            await File.AppendAllTextAsync(Path.Combine(workspace, "README.md"), "first patch line\n");

            var firstFinish = await RunCliAsync("patch", "finish", "--run", "aa-rerun", "--runs-root", runsRoot, "--skip-test", "--json");
            Assert.AreEqual(0, firstFinish.ExitCode, firstFinish.StandardOutput + firstFinish.StandardError);
            var firstPatch = await File.ReadAllTextAsync(Path.Combine(runPath, "patch.diff"));
            StringAssert.Contains(firstPatch, "first patch line");

            await File.AppendAllTextAsync(Path.Combine(workspace, "README.md"), "second patch line\n");
            var secondFinish = await RunCliAsync("patch", "finish", "--run", "aa-rerun", "--runs-root", runsRoot, "--skip-test", "--json");
            Assert.AreEqual(0, secondFinish.ExitCode, secondFinish.StandardOutput + secondFinish.StandardError);
            var secondPatch = await File.ReadAllTextAsync(Path.Combine(runPath, "patch.diff"));
            StringAssert.Contains(secondPatch, "second patch line");

            var sourceReadme = await File.ReadAllTextAsync(Path.Combine(repo, "README.md"));
            Assert.IsFalse(sourceReadme.Contains("first patch line", StringComparison.Ordinal));
            Assert.IsFalse(sourceReadme.Contains("second patch line", StringComparison.Ordinal));
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static async Task<string> CreateSourceRepoAsync(string root, bool includeProfiles)
    {
        var repo = Path.Combine(root, "source");
        Directory.CreateDirectory(repo);
        await File.WriteAllTextAsync(Path.Combine(repo, "README.md"), "initial source\n");
        await File.WriteAllTextAsync(Path.Combine(root, "task.md"), "# Patch task\n\nMake the safest small patch.\n");

        if (includeProfiles)
        {
            Directory.CreateDirectory(Path.Combine(repo, ".irondev"));
            await File.WriteAllTextAsync(
                Path.Combine(repo, ".irondev", "test-profiles.json"),
                """
                {
                  "profiles": {
                    "quick": "dotnet --version"
                  }
                }
                """);
        }

        await GitAsync(repo, "init");
        await GitAsync(repo, "config", "user.email", "block-aa@example.test");
        await GitAsync(repo, "config", "user.name", "Block AA Test");
        await GitAsync(repo, "add", ".");
        await GitAsync(repo, "commit", "-m", "initial");
        return repo;
    }

    private static async Task<CommandResult> RunCliAsync(params string[] args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = FindRepoRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(Path.Combine(FindRepoRoot(), "tools", "IronDev.Cli", "IronDev.Cli.csproj"));
        startInfo.ArgumentList.Add("--");

        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        return await RunProcessAsync(startInfo);
    }

    private static async Task GitAsync(string workingDirectory, params string[] args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        var result = await RunProcessAsync(startInfo);
        Assert.AreEqual(0, result.ExitCode, result.StandardOutput + result.StandardError);
    }

    private static async Task<CommandResult> RunProcessAsync(ProcessStartInfo startInfo)
    {
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Failed to start {startInfo.FileName}.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new CommandResult(process.ExitCode, await stdout, await stderr);
    }

    private static string ReadRunString(string runPath, string propertyName)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(runPath, "run.json")));
        return document.RootElement.GetProperty(propertyName).GetString() ?? string.Empty;
    }

    private static bool ReadRunBool(string runPath, string propertyName)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(runPath, "run.json")));
        return document.RootElement.GetProperty(propertyName).GetBoolean();
    }

    private static int ReadRunInt(string runPath, string propertyName)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(runPath, "run.json")));
        return document.RootElement.GetProperty(propertyName).GetInt32();
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "irondev-block-aa-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Test cleanup best effort only.
        }
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")) &&
                File.Exists(Path.Combine(directory.FullName, "tools", "IronDev.Cli", "IronDev.Cli.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate IronDev solution root.");
    }

    private sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError);
}
