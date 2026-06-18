using System.Diagnostics;
using System.Text;
using System.Text.Json;
using IronDev.Cli;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockZManualPatchProposalProductTests
{
    [TestMethod]
    public async Task PatchStartAndFinish_CreateReviewPackageWithoutMutatingSourceRepository()
    {
        var root = CreateTempDirectory();
        var repo = Path.Combine(root, "source-repo");
        var runsRoot = Path.Combine(root, "runs");
        var task = Path.Combine(root, "task.md");
        await CreateGitRepositoryAsync(repo);
        await File.WriteAllTextAsync(task, "Add a friendly sentence to the readme.");

        var startOutput = new StringWriter();
        var startError = new StringWriter();
        var start = await IronDevCli.RunAsync(
            [
                "patch", "start",
                "--repo", repo,
                "--task", task,
                "--test", "dotnet --version",
                "--runs-root", runsRoot,
                "--run-id", "block-z-test-run",
                "--json"
            ],
            startOutput,
            startError,
            CancellationToken.None);

        Assert.AreEqual(0, start, startError.ToString());
        var runPath = ReadString(startOutput.ToString(), "data", "runPath");
        var workspacePath = ReadString(startOutput.ToString(), "data", "workspacePath");
        Assert.IsTrue(Directory.Exists(workspacePath));
        Assert.IsTrue(File.Exists(Path.Combine(runPath, "task.md")));

        await File.AppendAllTextAsync(Path.Combine(workspacePath, "README.md"), Environment.NewLine + "Manual patch proposal product is alive.");
        await File.WriteAllTextAsync(Path.Combine(workspacePath, "notes.txt"), "New note from disposable workspace.");

        var finishOutput = new StringWriter();
        var finishError = new StringWriter();
        var finish = await IronDevCli.RunAsync(
            [
                "patch", "finish",
                "--run", runPath,
                "--test", "dotnet --version",
                "--json"
            ],
            finishOutput,
            finishError,
            CancellationToken.None);

        Assert.AreEqual(0, finish, finishError.ToString());
        AssertArtifactExists(runPath, "run.json");
        AssertArtifactExists(runPath, "patch.diff");
        AssertArtifactExists(runPath, "changed-files.txt");
        AssertArtifactExists(runPath, "test-results.txt");
        AssertArtifactExists(runPath, "review-summary.md");
        AssertArtifactExists(runPath, "known-risks.md");
        AssertArtifactExists(runPath, "manual-apply-instructions.md");

        var patch = await File.ReadAllTextAsync(Path.Combine(runPath, "patch.diff"));
        StringAssert.Contains(patch, "Manual patch proposal product is alive.");
        StringAssert.Contains(patch, "notes.txt");

        var changedFiles = await File.ReadAllTextAsync(Path.Combine(runPath, "changed-files.txt"));
        StringAssert.Contains(changedFiles, "README.md");
        StringAssert.Contains(changedFiles, "notes.txt");

        var sourceReadme = await File.ReadAllTextAsync(Path.Combine(repo, "README.md"));
        Assert.AreEqual("Initial readme.", sourceReadme.Trim());
        Assert.IsFalse(File.Exists(Path.Combine(repo, "notes.txt")));
        Assert.AreEqual(string.Empty, (await GitAsync(repo, ["status", "--porcelain=v1"])).Stdout.Trim());

        var summary = await File.ReadAllTextAsync(Path.Combine(runPath, "review-summary.md"));
        StringAssert.Contains(summary, "review evidence only");
        StringAssert.Contains(summary, "source repository was not modified");

        var runJson = await File.ReadAllTextAsync(Path.Combine(runPath, "run.json"));
        using var runDocument = JsonDocument.Parse(runJson);
        Assert.AreEqual("Finished", runDocument.RootElement.GetProperty("status").GetString());
        Assert.IsFalse(runDocument.RootElement.GetProperty("sourceRepoMutated").GetBoolean());
        Assert.IsFalse(runDocument.RootElement.GetProperty("sourceApplied").GetBoolean());
        Assert.IsFalse(runDocument.RootElement.GetProperty("gitCommitCreated").GetBoolean());
        Assert.IsFalse(runDocument.RootElement.GetProperty("gitPushPerformed").GetBoolean());
    }

    [TestMethod]
    public async Task PatchFinish_WhenTestFails_StillWritesReviewArtifacts()
    {
        var root = CreateTempDirectory();
        var repo = Path.Combine(root, "source-repo");
        var runsRoot = Path.Combine(root, "runs");
        var task = Path.Combine(root, "task.md");
        await CreateGitRepositoryAsync(repo);
        await File.WriteAllTextAsync(task, "Make a tiny change.");

        var startOutput = new StringWriter();
        var start = await IronDevCli.RunAsync(
            [
                "patch", "start",
                "--repo", repo,
                "--task", task,
                "--test", "dotnet --version",
                "--runs-root", runsRoot,
                "--run-id", "block-z-failing-test",
                "--json"
            ],
            startOutput,
            new StringWriter(),
            CancellationToken.None);

        Assert.AreEqual(0, start);
        var runPath = ReadString(startOutput.ToString(), "data", "runPath");
        var workspacePath = ReadString(startOutput.ToString(), "data", "workspacePath");
        await File.AppendAllTextAsync(Path.Combine(workspacePath, "README.md"), Environment.NewLine + "Failing test still files evidence.");

        var finishOutput = new StringWriter();
        var finish = await IronDevCli.RunAsync(
            [
                "patch", "finish",
                "--run", runPath,
                "--test", "dotnet definitely-not-a-real-command",
                "--json"
            ],
            finishOutput,
            new StringWriter(),
            CancellationToken.None);

        Assert.AreEqual(1, finish);
        AssertArtifactExists(runPath, "patch.diff");
        AssertArtifactExists(runPath, "changed-files.txt");
        AssertArtifactExists(runPath, "test-results.txt");
        AssertArtifactExists(runPath, "review-summary.md");
        AssertArtifactExists(runPath, "known-risks.md");
        AssertArtifactExists(runPath, "manual-apply-instructions.md");

        var testResults = await File.ReadAllTextAsync(Path.Combine(runPath, "test-results.txt"));
        StringAssert.Contains(testResults, "Exit code:");

        var risks = await File.ReadAllTextAsync(Path.Combine(runPath, "known-risks.md"));
        StringAssert.Contains(risks, "non-zero exit code");
    }

    [TestMethod]
    public async Task PatchStart_RejectsRunsRootInsideSourceRepository()
    {
        var root = CreateTempDirectory();
        var repo = Path.Combine(root, "source-repo");
        var task = Path.Combine(root, "task.md");
        await CreateGitRepositoryAsync(repo);
        await File.WriteAllTextAsync(task, "Unsafe runs root check.");

        var output = new StringWriter();
        var error = new StringWriter();
        var result = await IronDevCli.RunAsync(
            [
                "patch", "start",
                "--repo", repo,
                "--task", task,
                "--test", "dotnet --version",
                "--runs-root", Path.Combine(repo, ".runs"),
                "--json"
            ],
            output,
            error,
            CancellationToken.None);

        Assert.AreEqual(1, result);
        StringAssert.Contains(output.ToString(), "runs root must be outside the source repository");
        Assert.IsFalse(Directory.Exists(Path.Combine(repo, ".runs")));
    }

    [TestMethod]
    public async Task PatchFinish_RejectsForbiddenSourceControlTestCommandBeforeMutation()
    {
        var root = CreateTempDirectory();
        var repo = Path.Combine(root, "source-repo");
        var runsRoot = Path.Combine(root, "runs");
        var task = Path.Combine(root, "task.md");
        await CreateGitRepositoryAsync(repo);
        await File.WriteAllTextAsync(task, "Reject unsafe test command.");

        var startOutput = new StringWriter();
        var start = await IronDevCli.RunAsync(
            [
                "patch", "start",
                "--repo", repo,
                "--task", task,
                "--test", "dotnet --version",
                "--runs-root", runsRoot,
                "--run-id", "block-z-forbidden-command",
                "--json"
            ],
            startOutput,
            new StringWriter(),
            CancellationToken.None);

        Assert.AreEqual(0, start);
        var runPath = ReadString(startOutput.ToString(), "data", "runPath");
        var workspacePath = ReadString(startOutput.ToString(), "data", "workspacePath");
        await File.AppendAllTextAsync(Path.Combine(workspacePath, "README.md"), Environment.NewLine + "Forbidden test command should not finish.");

        var finishOutput = new StringWriter();
        var finish = await IronDevCli.RunAsync(
            [
                "patch", "finish",
                "--run", runPath,
                "--test", "git push origin main",
                "--json"
            ],
            finishOutput,
            new StringWriter(),
            CancellationToken.None);

        Assert.AreEqual(1, finish);
        StringAssert.Contains(finishOutput.ToString(), "forbidden source-control or release action");
        Assert.IsFalse(File.Exists(Path.Combine(runPath, "patch.diff")));
    }

    [TestMethod]
    public async Task PatchStatus_ReadsRunAsReviewEvidenceOnly()
    {
        var root = CreateTempDirectory();
        var repo = Path.Combine(root, "source-repo");
        var runsRoot = Path.Combine(root, "runs");
        var task = Path.Combine(root, "task.md");
        await CreateGitRepositoryAsync(repo);
        await File.WriteAllTextAsync(task, "Status command check.");

        var startOutput = new StringWriter();
        var start = await IronDevCli.RunAsync(
            [
                "patch", "start",
                "--repo", repo,
                "--task", task,
                "--test", "dotnet --version",
                "--runs-root", runsRoot,
                "--run-id", "block-z-status",
                "--json"
            ],
            startOutput,
            new StringWriter(),
            CancellationToken.None);

        Assert.AreEqual(0, start);

        var statusOutput = new StringWriter();
        var status = await IronDevCli.RunAsync(
            [
                "patch", "status",
                "--run", "block-z-status",
                "--runs-root", runsRoot,
                "--json"
            ],
            statusOutput,
            new StringWriter(),
            CancellationToken.None);

        Assert.AreEqual(0, status);
        using var doc = JsonDocument.Parse(statusOutput.ToString());
        var data = doc.RootElement.GetProperty("data");
        Assert.AreEqual("Started", data.GetProperty("status").GetString());
        Assert.IsFalse(data.GetProperty("sourceRepoMutated").GetBoolean());
        Assert.IsFalse(data.GetProperty("sourceApplied").GetBoolean());
        Assert.IsTrue(data.GetProperty("boundary").GetProperty("reviewPackageOnly").GetBoolean());
    }

    private static async Task CreateGitRepositoryAsync(string repo)
    {
        Directory.CreateDirectory(repo);
        await GitAsync(repo, ["init"]);
        await GitAsync(repo, ["config", "user.email", "irondev-tests@example.invalid"]);
        await GitAsync(repo, ["config", "user.name", "IronDev Tests"]);
        await File.WriteAllTextAsync(Path.Combine(repo, "README.md"), "Initial readme.");
        await GitAsync(repo, ["add", "README.md"]);
        await GitAsync(repo, ["commit", "-m", "initial"]);
    }

    private static async Task<ProcessResult> GitAsync(string workingDirectory, string[] arguments)
    {
        var result = await RunProcessAsync("git", workingDirectory, arguments);
        Assert.AreEqual(0, result.ExitCode, result.Stderr);
        return result;
    }

    private static async Task<ProcessResult> RunProcessAsync(string fileName, string workingDirectory, string[] arguments)
    {
        var output = new StringBuilder();
        var error = new StringBuilder();
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, item) =>
        {
            if (item.Data is not null)
                output.AppendLine(item.Data);
        };
        process.ErrorDataReceived += (_, item) =>
        {
            if (item.Data is not null)
                error.AppendLine(item.Data);
        };

        Assert.IsTrue(process.Start());
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();
        return new ProcessResult(process.ExitCode, output.ToString(), error.ToString());
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"irondev-block-z-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void AssertArtifactExists(string runPath, string fileName) =>
        Assert.IsTrue(File.Exists(Path.Combine(runPath, fileName)), $"Expected artifact '{fileName}' in '{runPath}'.");

    private static string ReadString(string json, string firstProperty, string secondProperty)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty(firstProperty).GetProperty(secondProperty).GetString()
               ?? throw new AssertFailedException($"Missing JSON property {firstProperty}.{secondProperty}.");
    }

    private sealed record ProcessResult(int ExitCode, string Stdout, string Stderr);
}
