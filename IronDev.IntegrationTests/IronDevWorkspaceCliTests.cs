using System.Diagnostics;
using System.Text.Json;
using IronDev.Cli;
using IronDev.Core.Workspaces;
using IronDev.Infrastructure.Services.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

public sealed partial class IronDevCliTests
{
    [TestMethod]
    public async Task WorkspaceCheck_MissingRequiredOptions_WithJson_ReturnsFailureEnvelope()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var result = await IronDevCli.RunAsync(
            ["workspace", "check", "--json"],
            output,
            error,
            handler: null,
            CancellationToken.None);

        Assert.AreEqual(2, result, error.ToString());
        AssertJsonWasWritten(output);

        using var doc = JsonDocument.Parse(output.ToString());
        var root = doc.RootElement;
        Assert.AreEqual("failed", root.GetProperty("status").GetString());
        Assert.AreEqual("workspace check", root.GetProperty("command").GetString());
        Assert.AreEqual(JsonValueKind.Object, root.GetProperty("data").ValueKind);
        AssertArrayNotEmpty(root.GetProperty("errors"));
    }

    [TestMethod]
    public async Task WorkspaceCheck_ValidWorkspacePath_ReturnsSucceededReadyEnvelope()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-check-valid");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspaceRoot = Path.Combine(testRoot, "workspaces");
            Directory.CreateDirectory(workspaceRoot);

            var output = new StringWriter();
            var error = new StringWriter();
            var result = await IronDevCli.RunAsync(
                [
                    "workspace", "check",
                    "--run-id", "run-123",
                    "--source-repo", sourceRepo,
                    "--workspace-root", workspaceRoot,
                    "--json"
                ],
                output,
                error,
                handler: null,
                CancellationToken.None);

            Assert.AreEqual(0, result, error.ToString());
            using var doc = JsonDocument.Parse(output.ToString());
            var root = doc.RootElement;
            Assert.AreEqual("succeeded", root.GetProperty("status").GetString());
            Assert.AreEqual("workspace check", root.GetProperty("command").GetString());
            Assert.AreEqual(JsonValueKind.Null, root.GetProperty("traceId").ValueKind);
            Assert.AreEqual(0, root.GetProperty("errors").GetArrayLength());

            var data = root.GetProperty("data");
            Assert.AreEqual("run-123", data.GetProperty("runId").GetString());
            Assert.AreEqual(Path.GetFullPath(sourceRepo), data.GetProperty("sourceRepo").GetString());
            Assert.AreEqual(Path.GetFullPath(workspaceRoot), data.GetProperty("workspaceRoot").GetString());
            Assert.AreEqual(Path.Combine(Path.GetFullPath(workspaceRoot), "run-123"), data.GetProperty("workspacePath").GetString());
            Assert.IsTrue(data.GetProperty("sourceRepoExists").GetBoolean());
            Assert.IsTrue(data.GetProperty("workspaceRootExists").GetBoolean());
            Assert.IsFalse(data.GetProperty("workspacePathExists").GetBoolean());
            Assert.IsFalse(data.GetProperty("isInsideSourceRepo").GetBoolean());
            Assert.IsTrue(data.GetProperty("gitStatusClean").GetBoolean());
            Assert.IsTrue(data.GetProperty("canCreateWorkspaceDirectory").GetBoolean());
            Assert.IsTrue(data.GetProperty("ready").GetBoolean());
            AssertArrayNotEmpty(data.GetProperty("checks"));
            Assert.IsFalse(Directory.Exists(Path.Combine(workspaceRoot, "run-123")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceCheck_WorkspaceInsideSourceRepo_ReturnsBlocked()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-check-inside-source");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspaceRoot = Path.Combine(sourceRepo, "workspaces");
            Directory.CreateDirectory(workspaceRoot);

            using var doc = await RunWorkspaceCheckAsync("run-123", sourceRepo, workspaceRoot, expectedExitCode: 1);
            var root = doc.RootElement;
            Assert.AreEqual("blocked", root.GetProperty("status").GetString());
            var data = root.GetProperty("data");
            Assert.IsFalse(data.GetProperty("ready").GetBoolean());
            Assert.IsTrue(data.GetProperty("isInsideSourceRepo").GetBoolean());
            AssertArrayNotEmpty(root.GetProperty("errors"));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceCheck_WorkspaceRootSameAsSourceRepo_ReturnsBlocked()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-check-root-same");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);

            using var doc = await RunWorkspaceCheckAsync("run-123", sourceRepo, sourceRepo, expectedExitCode: 1);
            var root = doc.RootElement;
            Assert.AreEqual("blocked", root.GetProperty("status").GetString());
            var data = root.GetProperty("data");
            Assert.IsFalse(data.GetProperty("ready").GetBoolean());
            Assert.IsTrue(data.GetProperty("workspaceRootSameAsSourceRepo").GetBoolean());
            AssertArrayNotEmpty(root.GetProperty("errors"));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceCheck_MissingSourceRepo_ReturnsFailed()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-check-missing-source");
        try
        {
            var sourceRepo = Path.Combine(testRoot, "missing-source");
            var workspaceRoot = Path.Combine(testRoot, "workspaces");
            Directory.CreateDirectory(workspaceRoot);

            using var doc = await RunWorkspaceCheckAsync("run-123", sourceRepo, workspaceRoot, expectedExitCode: 1);
            var root = doc.RootElement;
            Assert.AreEqual("failed", root.GetProperty("status").GetString());
            var data = root.GetProperty("data");
            Assert.IsFalse(data.GetProperty("sourceRepoExists").GetBoolean());
            Assert.IsFalse(data.GetProperty("ready").GetBoolean());
            AssertArrayNotEmpty(root.GetProperty("errors"));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceCheck_DirtyGitStatus_ReturnsBlocked()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-check-dirty");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            await File.WriteAllTextAsync(Path.Combine(sourceRepo, "dirty.txt"), "untracked");
            var workspaceRoot = Path.Combine(testRoot, "workspaces");
            Directory.CreateDirectory(workspaceRoot);

            using var doc = await RunWorkspaceCheckAsync("run-123", sourceRepo, workspaceRoot, expectedExitCode: 1);
            var root = doc.RootElement;
            Assert.AreEqual("blocked", root.GetProperty("status").GetString());
            var data = root.GetProperty("data");
            Assert.IsFalse(data.GetProperty("gitStatusClean").GetBoolean());
            Assert.IsFalse(data.GetProperty("ready").GetBoolean());
            AssertArrayNotEmpty(root.GetProperty("errors"));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceCheck_NonEmptyWorkspacePath_ReturnsBlocked()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-check-nonempty");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspaceRoot = Path.Combine(testRoot, "workspaces");
            var workspacePath = Path.Combine(workspaceRoot, "run-123");
            Directory.CreateDirectory(workspacePath);
            await File.WriteAllTextAsync(Path.Combine(workspacePath, "existing.txt"), "existing");

            using var doc = await RunWorkspaceCheckAsync("run-123", sourceRepo, workspaceRoot, expectedExitCode: 1);
            var root = doc.RootElement;
            Assert.AreEqual("blocked", root.GetProperty("status").GetString());
            var data = root.GetProperty("data");
            Assert.IsTrue(data.GetProperty("workspacePathExists").GetBoolean());
            Assert.IsFalse(data.GetProperty("ready").GetBoolean());
            AssertArrayNotEmpty(root.GetProperty("errors"));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceCheck_JsonOutput_UsesStandardEnvelope()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-check-envelope");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspaceRoot = Path.Combine(testRoot, "workspaces");
            Directory.CreateDirectory(workspaceRoot);

            using var doc = await RunWorkspaceCheckAsync("run-123", sourceRepo, workspaceRoot, expectedExitCode: 0);
            var root = doc.RootElement;
            var expectedTopLevelKeys = new[] { "status", "command", "traceId", "summary", "data", "errors", "warnings" };
            var topLevelProperties = root.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);

            CollectionAssert.AreEqual(
                expectedTopLevelKeys.OrderBy(item => item).ToArray(),
                topLevelProperties.OrderBy(item => item).ToArray());
            Assert.AreEqual("workspace check", root.GetProperty("command").GetString());
            Assert.IsFalse(root.TryGetProperty("loopReport", out _));
            Assert.IsFalse(root.TryGetProperty("processRun", out _));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspacePrepare_ReadinessFailure_DoesNotCreateWorkspace()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-prepare-readiness-failure");
        try
        {
            var sourceRepo = Path.Combine(testRoot, "missing-source");
            var workspaceRoot = Path.Combine(testRoot, "workspaces");
            Directory.CreateDirectory(workspaceRoot);

            using var doc = await RunWorkspacePrepareAsync("run-123", sourceRepo, workspaceRoot, expectedExitCode: 1);
            var root = doc.RootElement;
            Assert.AreEqual("failed", root.GetProperty("status").GetString());
            Assert.AreEqual("workspace prepare", root.GetProperty("command").GetString());
            var data = root.GetProperty("data");
            Assert.AreEqual("failed", data.GetProperty("readinessStatus").GetString());
            Assert.IsFalse(data.GetProperty("prepared").GetBoolean());
            Assert.IsFalse(Directory.Exists(data.GetProperty("workspacePath").GetString()));
            AssertArrayNotEmpty(root.GetProperty("errors"));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspacePrepare_SucceedsAfterReadinessPassesAndWritesMetadata()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-prepare-success");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryWithTrackedFilesAsync(testRoot);
            var workspaceRoot = Path.Combine(testRoot, "workspaces");
            Directory.CreateDirectory(workspaceRoot);

            using var doc = await RunWorkspacePrepareAsync("run-123", sourceRepo, workspaceRoot, expectedExitCode: 0);
            var root = doc.RootElement;
            Assert.AreEqual("succeeded", root.GetProperty("status").GetString());
            Assert.AreEqual("workspace prepare", root.GetProperty("command").GetString());
            Assert.AreEqual(0, root.GetProperty("errors").GetArrayLength());

            var data = root.GetProperty("data");
            var workspacePath = data.GetProperty("workspacePath").GetString()!;
            var metadataPath = data.GetProperty("metadataPath").GetString()!;
            Assert.AreEqual("succeeded", data.GetProperty("readinessStatus").GetString());
            Assert.IsTrue(data.GetProperty("prepared").GetBoolean());
            Assert.AreEqual("copy", data.GetProperty("preparationMethod").GetString());
            Assert.AreEqual(2, data.GetProperty("filesCopied").GetInt32());
            Assert.IsFalse(data.GetProperty("sourceRepoMutated").GetBoolean());
            AssertArrayNotEmpty(data.GetProperty("checks"));

            Assert.IsTrue(Directory.Exists(workspacePath));
            Assert.IsTrue(workspacePath.StartsWith(Path.GetFullPath(workspaceRoot), StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(workspacePath.StartsWith(Path.GetFullPath(sourceRepo), StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(File.Exists(Path.Combine(workspacePath, "README.md")));
            Assert.IsTrue(File.Exists(Path.Combine(workspacePath, "src", "app.cs")));
            Assert.IsTrue(File.Exists(metadataPath));

            using var metadata = JsonDocument.Parse(await File.ReadAllTextAsync(metadataPath));
            var metadataRoot = metadata.RootElement;
            Assert.AreEqual("run-123", metadataRoot.GetProperty("runId").GetString());
            Assert.AreEqual(Path.GetFullPath(sourceRepo), metadataRoot.GetProperty("sourceRepo").GetString());
            Assert.AreEqual(workspacePath, metadataRoot.GetProperty("workspacePath").GetString());
            Assert.AreEqual("copy", metadataRoot.GetProperty("preparationMethod").GetString());
            Assert.IsFalse(metadataRoot.GetProperty("sourceRepoMutated").GetBoolean());

            Assert.AreEqual(string.Empty, await GetGitStatusAsync(sourceRepo));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspacePrepare_ExcludesJunkDirectoriesFromCopy()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-prepare-exclusions");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryWithTrackedFilesAsync(testRoot);
            var workspaceRoot = Path.Combine(testRoot, "workspaces");
            Directory.CreateDirectory(workspaceRoot);

            using var doc = await RunWorkspacePrepareAsync("run-123", sourceRepo, workspaceRoot, expectedExitCode: 0);
            var workspacePath = doc.RootElement.GetProperty("data").GetProperty("workspacePath").GetString()!;

            Assert.IsFalse(Directory.Exists(Path.Combine(workspacePath, ".git")));
            Assert.IsFalse(Directory.Exists(Path.Combine(workspacePath, "bin")));
            Assert.IsFalse(Directory.Exists(Path.Combine(workspacePath, "obj")));
            Assert.IsFalse(Directory.Exists(Path.Combine(workspacePath, ".vs")));
            Assert.IsFalse(Directory.Exists(Path.Combine(workspacePath, "TestResults")));
            Assert.IsFalse(Directory.Exists(Path.Combine(workspacePath, "tools", "dogfood", "runs")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspacePrepare_JsonOutput_UsesStandardEnvelope()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-prepare-envelope");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryWithTrackedFilesAsync(testRoot);
            var workspaceRoot = Path.Combine(testRoot, "workspaces");
            Directory.CreateDirectory(workspaceRoot);

            using var doc = await RunWorkspacePrepareAsync("run-123", sourceRepo, workspaceRoot, expectedExitCode: 0);
            var root = doc.RootElement;
            var expectedTopLevelKeys = new[] { "status", "command", "traceId", "summary", "data", "errors", "warnings" };
            var topLevelProperties = root.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);

            CollectionAssert.AreEqual(
                expectedTopLevelKeys.OrderBy(item => item).ToArray(),
                topLevelProperties.OrderBy(item => item).ToArray());
            Assert.AreEqual("workspace prepare", root.GetProperty("command").GetString());
            Assert.IsFalse(root.TryGetProperty("loopReport", out _));
            Assert.IsFalse(root.TryGetProperty("processRun", out _));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public void WorkspacePrepareService_MustNotRunBuildTestOrAgentCommands()
    {
        var repoRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repoRoot,
            "IronDev.Infrastructure",
            "Services",
            "Workspaces",
            "DisposableWorkspacePrepareService.cs"));

        Assert.IsFalse(source.Contains("ProcessStartInfo", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("Process.Start", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("RunCommandAsync", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("dotnet", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(source.Contains("IAgent", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("SupervisorAgent", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("TesterAgent", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("QualityAgent", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task WorkspaceRun_UnknownCommand_ReturnsBlockedAndDoesNotStartProcess()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-run-unknown-command");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);

            using var doc = await RunWorkspaceCommandAsync("run-1", workspacePath, "powershell", expectedExitCode: 1);
            var root = doc.RootElement;
            Assert.AreEqual("blocked", root.GetProperty("status").GetString());
            Assert.AreEqual("workspace run", root.GetProperty("command").GetString());
            AssertArrayNotEmpty(root.GetProperty("errors"));
            AssertStringArrayContains(root.GetProperty("errors"), "not allowlisted");
            Assert.IsFalse(Directory.Exists(Path.Combine(workspacePath, ".irondev", "runs", "run-1", "powershell")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceRun_MissingMetadata_BlocksExecution()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-run-missing-metadata");
        try
        {
            var workspacePath = Path.Combine(testRoot, "workspace");
            Directory.CreateDirectory(workspacePath);

            using var doc = await RunWorkspaceCommandAsync("run-1", workspacePath, "dotnet-build", expectedExitCode: 1);
            var root = doc.RootElement;
            Assert.AreEqual("blocked", root.GetProperty("status").GetString());
            AssertStringArrayContains(root.GetProperty("errors"), "metadata");
            Assert.IsFalse(Directory.Exists(Path.Combine(workspacePath, ".irondev", "runs")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceRun_RunIdMismatch_BlocksExecution()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-run-runid-mismatch");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-a", sourceRepo);

            using var doc = await RunWorkspaceCommandAsync("run-b", workspacePath, "dotnet-build", expectedExitCode: 1);
            var root = doc.RootElement;
            Assert.AreEqual("blocked", root.GetProperty("status").GetString());
            AssertStringArrayContains(root.GetProperty("errors"), "runId mismatch");
            Assert.IsFalse(Directory.Exists(Path.Combine(workspacePath, ".irondev", "runs", "run-b", "dotnet-build")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceRun_MetadataMissingSourceRepo_BlocksExecution()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-run-missing-source-repo");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            await WriteWorkspaceMetadataAsync("run-1", sourceRepo, workspacePath, includeSourceRepo: false);

            using var doc = await RunWorkspaceCommandAsync("run-1", workspacePath, "dotnet-build", expectedExitCode: 1);
            var root = doc.RootElement;
            Assert.AreEqual("blocked", root.GetProperty("status").GetString());
            AssertStringArrayContains(root.GetProperty("errors"), "missing sourceRepo");
            Assert.IsFalse(Directory.Exists(Path.Combine(workspacePath, ".irondev", "runs", "run-1", "dotnet-build")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceRun_MetadataMissingWorkspacePath_BlocksExecution()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-run-missing-workspace-path");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            await WriteWorkspaceMetadataAsync("run-1", sourceRepo, workspacePath, includeWorkspacePath: false);

            using var doc = await RunWorkspaceCommandAsync("run-1", workspacePath, "dotnet-build", expectedExitCode: 1);
            var root = doc.RootElement;
            Assert.AreEqual("blocked", root.GetProperty("status").GetString());
            AssertStringArrayContains(root.GetProperty("errors"), "missing workspacePath");
            Assert.IsFalse(Directory.Exists(Path.Combine(workspacePath, ".irondev", "runs", "run-1", "dotnet-build")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceRun_MetadataSourceRepoEqualsWorkspace_BlocksExecution()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-run-source-equals-workspace");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            await WriteWorkspaceMetadataAsync("run-1", workspacePath, workspacePath);

            using var doc = await RunWorkspaceCommandAsync("run-1", workspacePath, "dotnet-build", expectedExitCode: 1);
            var root = doc.RootElement;
            Assert.AreEqual("blocked", root.GetProperty("status").GetString());
            AssertStringArrayContains(root.GetProperty("errors"), "isolated from the source repository");
            Assert.IsFalse(Directory.Exists(Path.Combine(workspacePath, ".irondev", "runs", "run-1", "dotnet-build")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceRun_CommandTimeoutFailsClosed()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-run-timeout");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedDotnetWorkspaceAsync(testRoot, "run-1", sourceRepo, brokenProgram: false);
            var service = new DisposableWorkspaceCommandService(TimeSpan.Zero);

            var result = await service.RunAsync(
                new DisposableWorkspaceCommandRequest
                {
                    RunId = "run-1",
                    WorkspacePath = workspacePath,
                    CommandId = "dotnet-build"
                },
                CancellationToken.None);

            Assert.AreEqual("failed", result.Status);
            Assert.AreEqual(1, result.ExitCode);
            Assert.AreEqual(-1, result.Data.ExitCode);
            Assert.IsFalse(result.Data.Succeeded);
            Assert.IsTrue(result.Errors.Any(error => error.Contains("timed out", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceRun_DotnetBuildSucceedsInPreparedWorkspaceAndWritesEvidence()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-run-build-success");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedDotnetWorkspaceAsync(testRoot, "run-1", sourceRepo, brokenProgram: false);

            using var doc = await RunWorkspaceCommandAsync("run-1", workspacePath, "dotnet-build", expectedExitCode: 0);
            var root = doc.RootElement;
            Assert.AreEqual("succeeded", root.GetProperty("status").GetString());
            Assert.AreEqual("workspace run", root.GetProperty("command").GetString());
            Assert.AreEqual(0, root.GetProperty("errors").GetArrayLength());

            var data = root.GetProperty("data");
            Assert.AreEqual("run-1", data.GetProperty("runId").GetString());
            Assert.AreEqual("dotnet-build", data.GetProperty("commandId").GetString());
            Assert.AreEqual(Path.GetFullPath(workspacePath), data.GetProperty("workingDirectory").GetString());
            Assert.AreEqual(0, data.GetProperty("exitCode").GetInt32());
            Assert.IsTrue(data.GetProperty("succeeded").GetBoolean());

            var stdoutPath = data.GetProperty("stdoutPath").GetString()!;
            var stderrPath = data.GetProperty("stderrPath").GetString()!;
            var commandMetadataPath = data.GetProperty("commandMetadataPath").GetString()!;
            Assert.IsTrue(File.Exists(stdoutPath));
            Assert.IsTrue(File.Exists(stderrPath));
            Assert.IsTrue(File.Exists(commandMetadataPath));
            Assert.AreEqual(3, data.GetProperty("evidencePaths").GetArrayLength());

            using var metadata = JsonDocument.Parse(await File.ReadAllTextAsync(commandMetadataPath));
            var commandMetadata = metadata.RootElement;
            Assert.AreEqual("run-1", commandMetadata.GetProperty("runId").GetString());
            Assert.AreEqual("dotnet-build", commandMetadata.GetProperty("commandId").GetString());
            Assert.AreEqual(Path.GetFullPath(workspacePath), commandMetadata.GetProperty("workingDirectory").GetString());
            Assert.AreEqual("dotnet", commandMetadata.GetProperty("executable").GetString());
            Assert.AreEqual(0, commandMetadata.GetProperty("exitCode").GetInt32());

            Assert.AreEqual(string.Empty, await GetGitStatusAsync(sourceRepo));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceRun_DotnetBuildFailureReturnsFailedEnvelopeAndEvidence()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-run-build-failure");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedDotnetWorkspaceAsync(testRoot, "run-1", sourceRepo, brokenProgram: true);

            using var doc = await RunWorkspaceCommandAsync("run-1", workspacePath, "dotnet-build", expectedExitCode: 1);
            var root = doc.RootElement;
            Assert.AreEqual("failed", root.GetProperty("status").GetString());
            AssertStringArrayContains(root.GetProperty("errors"), "non-zero exit code");

            var data = root.GetProperty("data");
            Assert.IsFalse(data.GetProperty("succeeded").GetBoolean());
            Assert.AreNotEqual(0, data.GetProperty("exitCode").GetInt32());
            Assert.IsTrue(File.Exists(data.GetProperty("stdoutPath").GetString()!));
            Assert.IsTrue(File.Exists(data.GetProperty("stderrPath").GetString()!));
            Assert.IsTrue(File.Exists(data.GetProperty("commandMetadataPath").GetString()!));
            Assert.AreEqual(3, data.GetProperty("evidencePaths").GetArrayLength());
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceRun_JsonOutput_UsesStandardEnvelope()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-run-envelope");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);

            using var doc = await RunWorkspaceCommandAsync("run-1", workspacePath, "unknown-command", expectedExitCode: 1);
            var root = doc.RootElement;
            var expectedTopLevelKeys = new[] { "status", "command", "traceId", "summary", "data", "errors", "warnings" };
            var topLevelProperties = root.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);

            CollectionAssert.AreEqual(
                expectedTopLevelKeys.OrderBy(item => item).ToArray(),
                topLevelProperties.OrderBy(item => item).ToArray());
            Assert.AreEqual("workspace run", root.GetProperty("command").GetString());
            Assert.IsFalse(root.TryGetProperty("loopReport", out _));
            Assert.IsFalse(root.TryGetProperty("processRun", out _));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public void WorkspaceCommandService_MustUseAllowlistedNoShellExecution()
    {
        var repoRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repoRoot,
            "IronDev.Infrastructure",
            "Services",
            "Workspaces",
            "DisposableWorkspaceCommandService.cs"));

        StringAssert.Contains(source, "UseShellExecute = false");
        StringAssert.Contains(source, "ArgumentList");
        Assert.IsFalse(source.Contains("cmd.exe", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(source.Contains("powershell", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(source.Contains("/bin/sh", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(source.Contains("-Command", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(source.Contains("IAgent", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("SupervisorAgent", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task WorkspaceValidate_UnknownProfile_ReturnsBlockedAndDoesNotRunCommands()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-validate-unknown-profile");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);

            using var doc = await RunWorkspaceValidateAsync("run-1", workspacePath, "arbitrary", expectedExitCode: 1);
            var root = doc.RootElement;
            Assert.AreEqual("blocked", root.GetProperty("status").GetString());
            Assert.AreEqual("workspace validate", root.GetProperty("command").GetString());
            AssertStringArrayContains(root.GetProperty("errors"), "profile");
            Assert.IsFalse(Directory.Exists(Path.Combine(workspacePath, ".irondev", "runs", "run-1", "dotnet-build")));
            Assert.IsFalse(File.Exists(Path.Combine(workspacePath, ".irondev", "runs", "run-1", "validation.json")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceValidate_DotnetBuildTestProfileSucceedsAndWritesPackage()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-validate-success");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedDotnetTestWorkspaceAsync(testRoot, "run-1", sourceRepo, brokenBuild: false);

            using var doc = await RunWorkspaceValidateAsync("run-1", workspacePath, "dotnet-build-test", expectedExitCode: 0);
            var root = doc.RootElement;
            Assert.AreEqual("succeeded", root.GetProperty("status").GetString());
            Assert.AreEqual("workspace validate", root.GetProperty("command").GetString());
            Assert.AreEqual(0, root.GetProperty("errors").GetArrayLength());

            var data = root.GetProperty("data");
            Assert.AreEqual("run-1", data.GetProperty("runId").GetString());
            Assert.AreEqual("dotnet-build-test", data.GetProperty("profileId").GetString());
            Assert.AreEqual("succeeded", data.GetProperty("status").GetString());
            Assert.IsTrue(data.GetProperty("succeeded").GetBoolean());

            var steps = data.GetProperty("steps").EnumerateArray().ToArray();
            Assert.AreEqual(2, steps.Length);
            Assert.AreEqual("dotnet-build", steps[0].GetProperty("commandId").GetString());
            Assert.AreEqual("succeeded", steps[0].GetProperty("status").GetString());
            Assert.AreEqual("dotnet-test", steps[1].GetProperty("commandId").GetString());
            Assert.AreEqual("succeeded", steps[1].GetProperty("status").GetString());

            var validationMetadataPath = data.GetProperty("validationMetadataPath").GetString()!;
            Assert.IsTrue(File.Exists(validationMetadataPath));
            Assert.IsTrue(data.GetProperty("evidencePaths").GetArrayLength() >= 7);
            Assert.IsTrue(File.Exists(Path.Combine(workspacePath, ".irondev", "runs", "run-1", "dotnet-build", "command.json")));
            Assert.IsTrue(File.Exists(Path.Combine(workspacePath, ".irondev", "runs", "run-1", "dotnet-test", "command.json")));

            using var metadata = JsonDocument.Parse(await File.ReadAllTextAsync(validationMetadataPath));
            var metadataRoot = metadata.RootElement;
            Assert.AreEqual("run-1", metadataRoot.GetProperty("runId").GetString());
            Assert.AreEqual("dotnet-build-test", metadataRoot.GetProperty("profileId").GetString());
            Assert.AreEqual("succeeded", metadataRoot.GetProperty("status").GetString());
            Assert.AreEqual(2, metadataRoot.GetProperty("steps").GetArrayLength());

            Assert.AreEqual(string.Empty, await GetGitStatusAsync(sourceRepo));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceValidate_BuildFailureStopsBeforeTest()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-validate-build-failure");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedDotnetTestWorkspaceAsync(testRoot, "run-1", sourceRepo, brokenBuild: true);

            using var doc = await RunWorkspaceValidateAsync("run-1", workspacePath, "dotnet-build-test", expectedExitCode: 1);
            var root = doc.RootElement;
            Assert.AreEqual("failed", root.GetProperty("status").GetString());
            AssertStringArrayContains(root.GetProperty("errors"), "non-zero exit code");

            var data = root.GetProperty("data");
            var steps = data.GetProperty("steps").EnumerateArray().ToArray();
            Assert.AreEqual(1, steps.Length);
            Assert.AreEqual("dotnet-build", steps[0].GetProperty("commandId").GetString());
            Assert.AreEqual("failed", steps[0].GetProperty("status").GetString());
            Assert.IsFalse(File.Exists(Path.Combine(workspacePath, ".irondev", "runs", "run-1", "dotnet-test", "command.json")));
            Assert.IsTrue(File.Exists(data.GetProperty("validationMetadataPath").GetString()!));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceValidate_CommandBlockedStopsValidation()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-validate-command-blocked");
        try
        {
            var workspacePath = Path.Combine(testRoot, "workspace");
            Directory.CreateDirectory(workspacePath);
            var commandService = new FakeWorkspaceCommandService
            {
                OnRunAsync = request => Task.FromResult(new DisposableWorkspaceCommandExecutionResult
                {
                    Status = "blocked",
                    Summary = "Blocked by fake command service.",
                    ExitCode = 1,
                    Data = new DisposableWorkspaceCommandData
                    {
                        RunId = request.RunId,
                        WorkspacePath = request.WorkspacePath,
                        CommandId = request.CommandId,
                        WorkingDirectory = request.WorkspacePath,
                        ExitCode = -1,
                        Succeeded = false,
                        EvidencePaths = [],
                        Errors = ["fake blocked command"],
                        Warnings = []
                    },
                    Errors = ["fake blocked command"],
                    Warnings = []
                })
            };
            var service = new DisposableWorkspaceValidationService(commandService);

            var result = await service.ValidateAsync(
                new DisposableWorkspaceValidationRequest
                {
                    RunId = "run-1",
                    WorkspacePath = workspacePath,
                    ProfileId = "dotnet-build-test"
                },
                CancellationToken.None);

            Assert.AreEqual("blocked", result.Status);
            Assert.AreEqual(1, result.Data.Steps.Count);
            Assert.AreEqual("dotnet-build", result.Data.Steps[0].CommandId);
            CollectionAssert.AreEqual(new[] { "dotnet-build" }, commandService.CommandIds.ToArray());
            Assert.IsTrue(File.Exists(result.Data.ValidationMetadataPath!));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceValidate_JsonOutput_UsesStandardEnvelope()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-validate-envelope");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);

            using var doc = await RunWorkspaceValidateAsync("run-1", workspacePath, "unknown-profile", expectedExitCode: 1);
            var root = doc.RootElement;
            var expectedTopLevelKeys = new[] { "status", "command", "traceId", "summary", "data", "errors", "warnings" };
            var topLevelProperties = root.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);

            CollectionAssert.AreEqual(
                expectedTopLevelKeys.OrderBy(item => item).ToArray(),
                topLevelProperties.OrderBy(item => item).ToArray());
            Assert.AreEqual("workspace validate", root.GetProperty("command").GetString());
            Assert.IsFalse(root.TryGetProperty("loopReport", out _));
            Assert.IsFalse(root.TryGetProperty("processRun", out _));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public void WorkspaceValidationService_MustNotStartProcessesDirectly()
    {
        var repoRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repoRoot,
            "IronDev.Infrastructure",
            "Services",
            "Workspaces",
            "DisposableWorkspaceValidationService.cs"));

        StringAssert.Contains(source, "IDisposableWorkspaceCommandService");
        Assert.IsFalse(source.Contains("ProcessStartInfo", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("Process.Start", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("cmd.exe", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(source.Contains("powershell", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(source.Contains("/bin/sh", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(source.Contains("-Command", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task WorkspaceDiff_MissingMetadata_BlocksDiff()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-diff-missing-metadata");
        try
        {
            var workspacePath = Path.Combine(testRoot, "workspace");
            Directory.CreateDirectory(workspacePath);

            using var doc = await RunWorkspaceDiffAsync("run-1", workspacePath, expectedExitCode: 1);
            var root = doc.RootElement;
            Assert.AreEqual("blocked", root.GetProperty("status").GetString());
            Assert.AreEqual("workspace diff", root.GetProperty("command").GetString());
            AssertStringArrayContains(root.GetProperty("errors"), "metadata");
            Assert.IsFalse(File.Exists(Path.Combine(workspacePath, ".irondev", "runs", "run-1", "diff.json")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceDiff_RunIdMismatch_BlocksDiff()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-diff-runid-mismatch");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-a", sourceRepo);

            using var doc = await RunWorkspaceDiffAsync("run-b", workspacePath, expectedExitCode: 1);
            var root = doc.RootElement;
            Assert.AreEqual("blocked", root.GetProperty("status").GetString());
            AssertStringArrayContains(root.GetProperty("errors"), "runId mismatch");
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceDiff_MetadataMissingSourceRepo_BlocksDiff()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-diff-missing-source-repo");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            await WriteWorkspaceMetadataAsync("run-1", sourceRepo, workspacePath, includeSourceRepo: false);

            using var doc = await RunWorkspaceDiffAsync("run-1", workspacePath, expectedExitCode: 1);
            var root = doc.RootElement;
            Assert.AreEqual("blocked", root.GetProperty("status").GetString());
            AssertStringArrayContains(root.GetProperty("errors"), "missing sourceRepo");
            Assert.IsFalse(File.Exists(Path.Combine(workspacePath, ".irondev", "runs", "run-1", "diff.json")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceDiff_MetadataMissingWorkspacePath_BlocksDiff()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-diff-missing-workspace-path");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            await WriteWorkspaceMetadataAsync("run-1", sourceRepo, workspacePath, includeWorkspacePath: false);

            using var doc = await RunWorkspaceDiffAsync("run-1", workspacePath, expectedExitCode: 1);
            var root = doc.RootElement;
            Assert.AreEqual("blocked", root.GetProperty("status").GetString());
            AssertStringArrayContains(root.GetProperty("errors"), "missing workspacePath");
            Assert.IsFalse(File.Exists(Path.Combine(workspacePath, ".irondev", "runs", "run-1", "diff.json")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceDiff_MetadataSourceRepoInsideWorkspace_BlocksDiff()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-diff-source-inside-workspace");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            var nestedSourceRepo = Path.Combine(workspacePath, "nested-source");
            Directory.CreateDirectory(nestedSourceRepo);
            await WriteWorkspaceMetadataAsync("run-1", nestedSourceRepo, workspacePath);

            using var doc = await RunWorkspaceDiffAsync("run-1", workspacePath, expectedExitCode: 1);
            var root = doc.RootElement;
            Assert.AreEqual("blocked", root.GetProperty("status").GetString());
            AssertStringArrayContains(root.GetProperty("errors"), "isolated");
            Assert.IsFalse(File.Exists(Path.Combine(workspacePath, ".irondev", "runs", "run-1", "diff.json")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceDiff_NoChanges_ReturnsSucceededUnchanged()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-diff-unchanged");
        try
        {
            var (sourceRepo, workspacePath) = await PrepareTrackedWorkspaceForDiffAsync(testRoot, "run-1");

            using var doc = await RunWorkspaceDiffAsync("run-1", workspacePath, expectedExitCode: 0);
            var root = doc.RootElement;
            Assert.AreEqual("succeeded", root.GetProperty("status").GetString());
            var data = root.GetProperty("data");
            Assert.IsFalse(data.GetProperty("changed").GetBoolean());
            Assert.AreEqual(0, data.GetProperty("addedFiles").GetArrayLength());
            Assert.AreEqual(0, data.GetProperty("modifiedFiles").GetArrayLength());
            Assert.AreEqual(0, data.GetProperty("deletedFiles").GetArrayLength());
            Assert.IsTrue(data.GetProperty("unchangedFileCount").GetInt32() > 0);
            Assert.IsTrue(File.Exists(data.GetProperty("diffMetadataPath").GetString()!));
            Assert.AreEqual(1, data.GetProperty("evidencePaths").GetArrayLength());
            Assert.AreEqual(string.Empty, await GetGitStatusAsync(sourceRepo));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceDiff_AddedFileIsDetected()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-diff-added");
        try
        {
            var (_, workspacePath) = await PrepareTrackedWorkspaceForDiffAsync(testRoot, "run-1");
            await File.WriteAllTextAsync(Path.Combine(workspacePath, "new-file.txt"), "new");

            using var doc = await RunWorkspaceDiffAsync("run-1", workspacePath, expectedExitCode: 0);
            var data = doc.RootElement.GetProperty("data");
            Assert.IsTrue(data.GetProperty("changed").GetBoolean());
            AssertStringArrayContains(data.GetProperty("addedFiles"), "new-file.txt");
            Assert.AreEqual("succeeded", doc.RootElement.GetProperty("status").GetString());
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceDiff_ModifiedFileIsDetected()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-diff-modified");
        try
        {
            var (_, workspacePath) = await PrepareTrackedWorkspaceForDiffAsync(testRoot, "run-1");
            await File.WriteAllTextAsync(Path.Combine(workspacePath, "README.md"), "changed");

            using var doc = await RunWorkspaceDiffAsync("run-1", workspacePath, expectedExitCode: 0);
            var data = doc.RootElement.GetProperty("data");
            Assert.IsTrue(data.GetProperty("changed").GetBoolean());
            AssertStringArrayContains(data.GetProperty("modifiedFiles"), "README.md");
            Assert.AreEqual("succeeded", doc.RootElement.GetProperty("status").GetString());
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceDiff_DeletedFileIsDetected()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-diff-deleted");
        try
        {
            var (_, workspacePath) = await PrepareTrackedWorkspaceForDiffAsync(testRoot, "run-1");
            File.Delete(Path.Combine(workspacePath, "README.md"));

            using var doc = await RunWorkspaceDiffAsync("run-1", workspacePath, expectedExitCode: 0);
            var data = doc.RootElement.GetProperty("data");
            Assert.IsTrue(data.GetProperty("changed").GetBoolean());
            AssertStringArrayContains(data.GetProperty("deletedFiles"), "README.md");
            Assert.AreEqual("succeeded", doc.RootElement.GetProperty("status").GetString());
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceDiff_IgnoresIronDevEvidence()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-diff-ignore-evidence");
        try
        {
            var (_, workspacePath) = await PrepareTrackedWorkspaceForDiffAsync(testRoot, "run-1");
            var evidencePath = Path.Combine(workspacePath, ".irondev", "runs", "run-1", "validation.json");
            Directory.CreateDirectory(Path.GetDirectoryName(evidencePath)!);
            await File.WriteAllTextAsync(evidencePath, "{}");

            using var doc = await RunWorkspaceDiffAsync("run-1", workspacePath, expectedExitCode: 0);
            var data = doc.RootElement.GetProperty("data");
            Assert.IsFalse(data.GetProperty("changed").GetBoolean());
            Assert.AreEqual(0, data.GetProperty("addedFiles").GetArrayLength());
            Assert.AreEqual("succeeded", doc.RootElement.GetProperty("status").GetString());
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceDiff_JsonOutput_UsesStandardEnvelope()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-diff-envelope");
        try
        {
            var (_, workspacePath) = await PrepareTrackedWorkspaceForDiffAsync(testRoot, "run-1");

            using var doc = await RunWorkspaceDiffAsync("run-1", workspacePath, expectedExitCode: 0);
            var root = doc.RootElement;
            var expectedTopLevelKeys = new[] { "status", "command", "traceId", "summary", "data", "errors", "warnings" };
            var topLevelProperties = root.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);

            CollectionAssert.AreEqual(
                expectedTopLevelKeys.OrderBy(item => item).ToArray(),
                topLevelProperties.OrderBy(item => item).ToArray());
            Assert.AreEqual("workspace diff", root.GetProperty("command").GetString());
            Assert.IsFalse(root.TryGetProperty("loopReport", out _));
            Assert.IsFalse(root.TryGetProperty("processRun", out _));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public void WorkspaceDiffService_MustNotExecuteProcesses()
    {
        var repoRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repoRoot,
            "IronDev.Infrastructure",
            "Services",
            "Workspaces",
            "DisposableWorkspaceDiffService.cs"));

        Assert.IsFalse(source.Contains("ProcessStartInfo", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("Process.Start", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("\"git\"", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(source.Contains("RunGit", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(source.Contains("cmd.exe", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(source.Contains("powershell", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(source.Contains("/bin/sh", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(source.Contains("IAgent", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task AgentRunSupervisor_MissingRequiredOptions_WithJson_ReturnsFailureEnvelope()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var result = await IronDevCli.RunAsync(
            ["agent", "run", "supervisor", "--json"],
            output,
            error,
            handler: null,
            cancellationToken: CancellationToken.None);

        Assert.AreEqual(2, result, error.ToString());
        AssertJsonWasWritten(output);

        using var doc = JsonDocument.Parse(output.ToString());
        var root = doc.RootElement;
        Assert.AreEqual("failed", root.GetProperty("status").GetString());
        Assert.AreEqual("agent run supervisor", root.GetProperty("command").GetString());
        AssertArrayNotEmpty(root.GetProperty("errors"));
        Assert.AreEqual(string.Empty, root.GetProperty("data").GetProperty("runId").GetString());
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AgentRunSupervisor_PassesRequestToService(bool liveLlm)
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var fakeService = new FakeSupervisorAgentRunService
        {
            OnRunAsync = (_, _) => Task.FromResult(BuildSupervisorRunResult(
                "succeeded",
                "succeeded",
                "not_required",
                false,
                0,
                "AgentRunProof001",
                "IronDev",
                "check current run health",
                "tools/dogfood/test-agent-plans/irondev-code-standards-alpha.json"))
        };

        var arguments = new List<string>
        {
            "agent", "run", "supervisor",
            "--project", "IronDev",
            "--query", "check current run health",
            "--plan", "tools/dogfood/test-agent-plans/irondev-code-standards-alpha.json",
            "--run-id", "AgentRunProof001",
            "--json"
        };
        if (liveLlm)
            arguments.AddRange(["--live-llm", "true"]);

        var result = await IronDevCli.RunAsync(
            arguments.ToArray(),
            output,
            error,
            handler: null,
            supervisorAgentRunService: fakeService,
            cancellationToken: CancellationToken.None);

        Assert.AreEqual(0, result, error.ToString());
        Assert.IsNotNull(fakeService.LastRequest);
        Assert.AreEqual("IronDev", fakeService.LastRequest!.Project);
        Assert.AreEqual("check current run health", fakeService.LastRequest.Query);
        Assert.AreEqual("tools/dogfood/test-agent-plans/irondev-code-standards-alpha.json", fakeService.LastRequest.PlanPath);
        Assert.AreEqual("AgentRunProof001", fakeService.LastRequest.RunId);
        Assert.AreEqual(liveLlm, fakeService.LastRequest.LiveLlm);
    }

    [DataTestMethod]
    [DataRow("succeeded", "Succeeded", "not_required", false, 0)]
    [DataRow("blocked", "Blocked", "required", true, 1)]
    [DataRow("failed", "Failed", "denied", false, 1)]
    public async Task AgentRunSupervisor_ServiceResult_StatusMapsToContractEnvelope(
        string serviceStatus,
        string expectedAgentStatus,
        string expectedApprovalDecision,
        bool expectedRequiresHumanApproval,
        int expectedExitCode)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var fakeService = new FakeSupervisorAgentRunService
        {
            OnRunAsync = (_, _) => Task.FromResult(BuildSupervisorRunResult(
                serviceStatus,
                serviceStatus,
                expectedApprovalDecision,
                expectedRequiresHumanApproval,
                expectedExitCode,
                "AgentRunProof001",
                "IronDev",
                "check current run health",
                "tools/dogfood/test-agent-plans/irondev-code-standards-alpha.json"))
        };

        var result = await IronDevCli.RunAsync(
            [
                "agent", "run", "supervisor",
                "--project", "IronDev",
                "--query", "check current run health",
                "--plan", "tools/dogfood/test-agent-plans/irondev-code-standards-alpha.json",
                "--run-id", "AgentRunProof001",
                "--json"
            ],
            output,
            error,
            handler: null,
            supervisorAgentRunService: fakeService,
            cancellationToken: CancellationToken.None);

        Assert.AreEqual(expectedExitCode, result, error.ToString());

        using var doc = JsonDocument.Parse(output.ToString());
        var root = doc.RootElement;
        Assert.AreEqual(serviceStatus, root.GetProperty("status").GetString());
        Assert.AreEqual("agent run supervisor", root.GetProperty("command").GetString());

        var data = root.GetProperty("data");
        Assert.AreEqual("SupervisorAgent", data.GetProperty("agent").GetString());
        Assert.AreEqual("AgentRunProof001", data.GetProperty("runId").GetString());
        Assert.AreEqual(expectedAgentStatus, data.GetProperty("agentStatus").GetString());
        Assert.AreEqual("report_ready", data.GetProperty("decision").GetString());
        Assert.AreEqual(serviceStatus, data.GetProperty("tester").GetProperty("commandStatus").GetString());

        var governance = data.GetProperty("tester").GetProperty("governance");
        Assert.AreEqual(expectedApprovalDecision, governance.GetProperty("approvalDecision").GetString());
        Assert.AreEqual(expectedRequiresHumanApproval, governance.GetProperty("requiresHumanApproval").GetBoolean());

        var failurePackage = data.GetProperty("failurePackage");
        if (serviceStatus == "succeeded")
        {
            Assert.AreEqual(JsonValueKind.Null, failurePackage.ValueKind);
        }
        else
        {
            Assert.AreEqual(JsonValueKind.Object, failurePackage.ValueKind);
            Assert.AreEqual("AgentRunProof001", failurePackage.GetProperty("runId").GetString());
            Assert.AreEqual(serviceStatus, failurePackage.GetProperty("status").GetString());
            Assert.AreEqual("report_ready", failurePackage.GetProperty("decision").GetString());
            Assert.AreEqual(serviceStatus, failurePackage.GetProperty("testerCommandStatus").GetString());
            AssertArrayNotEmpty(failurePackage.GetProperty("errors"));
            Assert.IsFalse(string.IsNullOrWhiteSpace(failurePackage.GetProperty("recommendedNextAction").GetString()));
            var recoveryPlan = failurePackage.GetProperty("recoveryPlan");
            Assert.AreEqual(JsonValueKind.Object, recoveryPlan.ValueKind);
            Assert.IsFalse(recoveryPlan.GetProperty("allowsPatching").GetBoolean());
            Assert.IsFalse(recoveryPlan.GetProperty("allowsExecution").GetBoolean());
            AssertArrayNotEmpty(recoveryPlan.GetProperty("proposedSteps"));
            AssertArrayNotEmpty(recoveryPlan.GetProperty("stopConditions"));
        }
    }

    [TestMethod]
    public async Task AgentRunSupervisor_SucceededRun_HasNoFailurePackage()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var fakeService = new FakeSupervisorAgentRunService
        {
            OnRunAsync = (_, _) => Task.FromResult(BuildSupervisorRunResult(
                "succeeded",
                "succeeded",
                "not_required",
                false,
                0,
                "AgentRunProof001",
                "IronDev",
                "check current run health",
                "tools/dogfood/test-agent-plans/irondev-code-standards-alpha.json"))
        };

        var result = await IronDevCli.RunAsync(
            [
                "agent", "run", "supervisor",
                "--project", "IronDev",
                "--query", "check current run health",
                "--plan", "tools/dogfood/test-agent-plans/irondev-code-standards-alpha.json",
                "--run-id", "AgentRunProof001",
                "--json"
            ],
            output,
            error,
            handler: null,
            supervisorAgentRunService: fakeService,
            cancellationToken: CancellationToken.None);

        Assert.AreEqual(0, result, error.ToString());
        using var doc = JsonDocument.Parse(output.ToString());
        var data = doc.RootElement.GetProperty("data");
        Assert.AreEqual(JsonValueKind.Null, data.GetProperty("failurePackage").ValueKind);
    }

    [TestMethod]
    public async Task AgentRunSupervisor_BlockedRun_IncludesFailurePackageWithoutAutoPatchRecommendation()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var fakeService = new FakeSupervisorAgentRunService
        {
            OnRunAsync = (_, _) => Task.FromResult(BuildSupervisorRunResult(
                "blocked",
                "blocked",
                "required",
                true,
                1,
                "AgentRunProof001",
                "IronDev",
                "check current run health",
                "tools/dogfood/test-agent-plans/irondev-code-standards-alpha.json"))
        };

        var result = await IronDevCli.RunAsync(
            [
                "agent", "run", "supervisor",
                "--project", "IronDev",
                "--query", "check current run health",
                "--plan", "tools/dogfood/test-agent-plans/irondev-code-standards-alpha.json",
                "--run-id", "AgentRunProof001",
                "--json"
            ],
            output,
            error,
            handler: null,
            supervisorAgentRunService: fakeService,
            cancellationToken: CancellationToken.None);

        Assert.AreEqual(1, result, error.ToString());
        using var doc = JsonDocument.Parse(output.ToString());
        var failurePackage = doc.RootElement.GetProperty("data").GetProperty("failurePackage");
        Assert.AreEqual("blocked", failurePackage.GetProperty("status").GetString());
        Assert.AreEqual("AwaitingHumanApproval", failurePackage.GetProperty("blockedReason").GetString());
        StringAssert.Contains(failurePackage.GetProperty("recommendedNextAction").GetString(), "Do not patch automatically");
        var recoveryPlan = failurePackage.GetProperty("recoveryPlan");
        Assert.IsFalse(recoveryPlan.GetProperty("allowsPatching").GetBoolean());
        Assert.IsFalse(recoveryPlan.GetProperty("allowsExecution").GetBoolean());
        AssertStringArrayContains(recoveryPlan.GetProperty("requiredHumanChecks"), "approval");
        AssertStringArrayContains(recoveryPlan.GetProperty("stopConditions"), "Do not patch automatically");
    }

    [TestMethod]
    public async Task AgentRunSupervisor_MissingTesterContract_FailurePackageRecommendsContractInspection()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var fakeService = new FakeSupervisorAgentRunService
        {
            OnRunAsync = (_, _) => Task.FromResult(BuildSupervisorRunResult(
                "failed",
                "not_available",
                "not_available",
                false,
                1,
                "AgentRunProof001",
                "IronDev",
                "check current run health",
                "tools/dogfood/test-agent-plans/irondev-code-standards-alpha.json"))
        };

        var result = await IronDevCli.RunAsync(
            [
                "agent", "run", "supervisor",
                "--project", "IronDev",
                "--query", "check current run health",
                "--plan", "tools/dogfood/test-agent-plans/irondev-code-standards-alpha.json",
                "--run-id", "AgentRunProof001",
                "--json"
            ],
            output,
            error,
            handler: null,
            supervisorAgentRunService: fakeService,
            cancellationToken: CancellationToken.None);

        Assert.AreEqual(1, result, error.ToString());
        using var doc = JsonDocument.Parse(output.ToString());
        var data = doc.RootElement.GetProperty("data");
        Assert.AreEqual("not_available", data.GetProperty("tester").GetProperty("commandStatus").GetString());
        var recommendedNextAction = data.GetProperty("failurePackage").GetProperty("recommendedNextAction").GetString();
        Assert.IsTrue(
            recommendedNextAction?.Contains("tester run output", StringComparison.OrdinalIgnoreCase) == true ||
            recommendedNextAction?.Contains("run-report contract", StringComparison.OrdinalIgnoreCase) == true,
            $"Unexpected recommended next action: {recommendedNextAction}");
        var recoveryPlan = data.GetProperty("failurePackage").GetProperty("recoveryPlan");
        StringAssert.Contains(recoveryPlan.GetProperty("problemSummary").GetString(), "Tester run-report contract");
        AssertStringArrayContains(recoveryPlan.GetProperty("proposedSteps"), "tester run output");
        AssertStringArrayContains(recoveryPlan.GetProperty("proposedSteps"), "run-report contract");
        Assert.IsFalse(recoveryPlan.GetProperty("allowsPatching").GetBoolean());
        Assert.IsFalse(recoveryPlan.GetProperty("allowsExecution").GetBoolean());
    }

    [TestMethod]
    public async Task AgentRunSupervisor_FailedTesterRecoveryPlan_TargetsEvidenceInspection()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var fakeService = new FakeSupervisorAgentRunService
        {
            OnRunAsync = (_, _) => Task.FromResult(BuildSupervisorRunResult(
                "failed",
                "failed",
                "denied",
                false,
                1,
                "AgentRunProof001",
                "IronDev",
                "check current run health",
                "tools/dogfood/test-agent-plans/irondev-code-standards-alpha.json"))
        };

        var result = await IronDevCli.RunAsync(
            [
                "agent", "run", "supervisor",
                "--project", "IronDev",
                "--query", "check current run health",
                "--plan", "tools/dogfood/test-agent-plans/irondev-code-standards-alpha.json",
                "--run-id", "AgentRunProof001",
                "--json"
            ],
            output,
            error,
            handler: null,
            supervisorAgentRunService: fakeService,
            cancellationToken: CancellationToken.None);

        Assert.AreEqual(1, result, error.ToString());
        using var doc = JsonDocument.Parse(output.ToString());
        var failurePackage = doc.RootElement.GetProperty("data").GetProperty("failurePackage");
        Assert.AreEqual("failed", failurePackage.GetProperty("testerCommandStatus").GetString());
        var recoveryPlan = failurePackage.GetProperty("recoveryPlan");
        AssertStringArrayContains(recoveryPlan.GetProperty("evidenceToInspect"), "logs/tester-evidence.log");
        AssertStringArrayContains(recoveryPlan.GetProperty("proposedSteps"), "evidence");
        AssertStringArrayContains(recoveryPlan.GetProperty("proposedSteps"), "failing build/test command");
        Assert.IsFalse(recoveryPlan.GetProperty("allowsPatching").GetBoolean());
        Assert.IsFalse(recoveryPlan.GetProperty("allowsExecution").GetBoolean());
    }

    [TestMethod]
    public async Task WorkspacePromotionPackage_MissingWorkspaceMetadata_BlocksPackage()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-promotion-missing-metadata");
        try
        {
            var workspacePath = Path.Combine(testRoot, "workspace");
            Directory.CreateDirectory(workspacePath);

            using var doc = await RunWorkspacePromotionPackageAsync("run-1", workspacePath, expectedExitCode: 1);
            var root = doc.RootElement;
            Assert.AreEqual("blocked", root.GetProperty("status").GetString());
            Assert.AreEqual("workspace promotion-package", root.GetProperty("command").GetString());
            AssertStringArrayContains(root.GetProperty("errors"), "metadata");
            Assert.IsFalse(File.Exists(Path.Combine(workspacePath, ".irondev", "runs", "run-1", "promotion-package.json")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspacePromotionPackage_MissingValidationPackage_BlocksPackage()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-promotion-missing-validation");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            await WriteDiffPackageAsync("run-1", workspacePath, sourceRepo, changed: true, addedFiles: ["src/new.cs"]);

            using var doc = await RunWorkspacePromotionPackageAsync("run-1", workspacePath, expectedExitCode: 1);
            var root = doc.RootElement;
            Assert.AreEqual("blocked", root.GetProperty("status").GetString());
            AssertStringArrayContains(root.GetProperty("errors"), "validation");
            Assert.IsFalse(File.Exists(Path.Combine(workspacePath, ".irondev", "runs", "run-1", "promotion-package.json")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspacePromotionPackage_MissingDiffPackage_BlocksPackage()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-promotion-missing-diff");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            await WriteValidationPackageAsync("run-1", workspacePath, "succeeded");

            using var doc = await RunWorkspacePromotionPackageAsync("run-1", workspacePath, expectedExitCode: 1);
            var root = doc.RootElement;
            Assert.AreEqual("blocked", root.GetProperty("status").GetString());
            AssertStringArrayContains(root.GetProperty("errors"), "diff");
            Assert.IsFalse(File.Exists(Path.Combine(workspacePath, ".irondev", "runs", "run-1", "promotion-package.json")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspacePromotionPackage_ValidationSucceededAndDiffChanged_CreatesReadyForHumanReviewPackage()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-promotion-ready");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            var commandEvidencePath = Path.Combine(workspacePath, ".irondev", "runs", "run-1", "dotnet-build", "stdout.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(commandEvidencePath)!);
            await File.WriteAllTextAsync(commandEvidencePath, "build output");
            await WriteValidationPackageAsync("run-1", workspacePath, "succeeded", [commandEvidencePath]);
            await WriteDiffPackageAsync("run-1", workspacePath, sourceRepo, changed: true, addedFiles: ["src/new.cs"], modifiedFiles: ["README.md"]);

            using var doc = await RunWorkspacePromotionPackageAsync("run-1", workspacePath, expectedExitCode: 0);
            var root = doc.RootElement;
            Assert.AreEqual("succeeded", root.GetProperty("status").GetString());
            Assert.AreEqual("workspace promotion-package", root.GetProperty("command").GetString());
            Assert.AreEqual(JsonValueKind.Null, root.GetProperty("traceId").ValueKind);
            Assert.AreEqual(JsonValueKind.Object, root.GetProperty("data").ValueKind);
            Assert.IsFalse(root.TryGetProperty("loopReport", out _));
            Assert.IsFalse(root.TryGetProperty("processRun", out _));

            var data = root.GetProperty("data");
            Assert.AreEqual("ready_for_human_review", data.GetProperty("recommendation").GetString());
            Assert.IsTrue(data.GetProperty("requiresHumanApproval").GetBoolean());
            Assert.IsFalse(data.GetProperty("canApplyToSourceRepo").GetBoolean());
            Assert.IsFalse(data.GetProperty("autoPromotionAllowed").GetBoolean());
            Assert.IsTrue(data.GetProperty("diffChanged").GetBoolean());
            AssertStringArrayContains(data.GetProperty("addedFiles"), "src/new.cs");
            AssertStringArrayContains(data.GetProperty("modifiedFiles"), "README.md");
            AssertStringArrayContains(data.GetProperty("evidencePaths"), "workspace.json");
            AssertStringArrayContains(data.GetProperty("evidencePaths"), "validation.json");
            AssertStringArrayContains(data.GetProperty("evidencePaths"), "diff.json");
            AssertStringArrayContains(data.GetProperty("evidencePaths"), "promotion-package.json");
            AssertStringArrayContains(data.GetProperty("evidencePaths"), "stdout.txt");
            Assert.IsTrue(File.Exists(data.GetProperty("promotionPackagePath").GetString()));
            Assert.IsFalse(File.Exists(Path.Combine(sourceRepo, ".irondev", "runs", "run-1", "promotion-package.json")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspacePromotionPackage_ValidationFailed_CreatesNotReadyPackage()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-promotion-validation-failed");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            await WriteValidationPackageAsync("run-1", workspacePath, "failed");
            await WriteDiffPackageAsync("run-1", workspacePath, sourceRepo, changed: true, modifiedFiles: ["README.md"]);

            using var doc = await RunWorkspacePromotionPackageAsync("run-1", workspacePath, expectedExitCode: 0);
            var data = doc.RootElement.GetProperty("data");
            Assert.AreEqual("not_ready_validation_failed", data.GetProperty("recommendation").GetString());
            Assert.IsTrue(data.GetProperty("requiresHumanApproval").GetBoolean());
            Assert.IsFalse(data.GetProperty("canApplyToSourceRepo").GetBoolean());
            AssertStringArrayContains(data.GetProperty("riskNotes"), "Validation did not succeed");
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspacePromotionPackage_NoChanges_CreatesNotReadyPackage()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-promotion-no-changes");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            await WriteValidationPackageAsync("run-1", workspacePath, "succeeded");
            await WriteDiffPackageAsync("run-1", workspacePath, sourceRepo, changed: false);

            using var doc = await RunWorkspacePromotionPackageAsync("run-1", workspacePath, expectedExitCode: 0);
            var data = doc.RootElement.GetProperty("data");
            Assert.AreEqual("not_ready_no_changes", data.GetProperty("recommendation").GetString());
            AssertStringArrayContains(data.GetProperty("riskNotes"), "No changed files");
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspacePromotionPackage_DeletedFiles_AddsRiskNote()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-promotion-deleted-risk");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            await WriteValidationPackageAsync("run-1", workspacePath, "succeeded");
            await WriteDiffPackageAsync("run-1", workspacePath, sourceRepo, changed: true, deletedFiles: ["old/file.cs"]);

            using var doc = await RunWorkspacePromotionPackageAsync("run-1", workspacePath, expectedExitCode: 0);
            var data = doc.RootElement.GetProperty("data");
            AssertStringArrayContains(data.GetProperty("deletedFiles"), "old/file.cs");
            AssertStringArrayContains(data.GetProperty("riskNotes"), "Deleted files");
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspacePromotionPackage_InvalidSourceWorkspaceRelationship_BlocksPackage()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-promotion-invalid-relationship");
        try
        {
            var workspacePath = Path.GetFullPath(Path.Combine(testRoot, "workspaces", "run-1"));
            var nestedSourceRepo = Path.Combine(workspacePath, "nested-source");
            Directory.CreateDirectory(nestedSourceRepo);
            await WriteWorkspaceMetadataAsync("run-1", nestedSourceRepo, workspacePath);

            using var doc = await RunWorkspacePromotionPackageAsync("run-1", workspacePath, expectedExitCode: 1);
            var root = doc.RootElement;
            Assert.AreEqual("blocked", root.GetProperty("status").GetString());
            AssertStringArrayContains(root.GetProperty("errors"), "isolated");
            Assert.IsFalse(File.Exists(Path.Combine(workspacePath, ".irondev", "runs", "run-1", "promotion-package.json")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public void DisposableWorkspacePromotionPackageService_DoesNotExecuteProcessesPatchOrAgents()
    {
        var serviceSource = File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "IronDev.Infrastructure", "Services", "Workspaces", "DisposableWorkspacePromotionPackageService.cs")));

        Assert.IsFalse(serviceSource.Contains("ProcessStartInfo", StringComparison.Ordinal));
        Assert.IsFalse(serviceSource.Contains("Process.Start", StringComparison.Ordinal));
        Assert.IsFalse(serviceSource.Contains("\"git\"", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(serviceSource.Contains("cmd.exe", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(serviceSource.Contains("powershell", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(serviceSource.Contains("/bin/sh", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(serviceSource.Contains("patch", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(serviceSource.Contains("ApplyAsync", StringComparison.Ordinal));
        Assert.IsFalse(serviceSource.Contains("ApplyPatch", StringComparison.Ordinal));
        Assert.IsFalse(serviceSource.Contains("IAgent", StringComparison.Ordinal));
        Assert.IsFalse(serviceSource.Contains("SupervisorAgent", StringComparison.Ordinal));
    }
}
