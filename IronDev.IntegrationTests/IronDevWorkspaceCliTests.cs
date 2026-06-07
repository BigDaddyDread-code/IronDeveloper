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
    [TestMethod]
    public async Task WorkspacePromotionApproval_MissingPromotionPackage_BlocksEvidence()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-approval-missing-package");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);

            using var doc = await RunWorkspacePromotionApprovalAsync(
                "run-1",
                workspacePath,
                "approved",
                "Rob",
                "Reviewed validation and diff package.",
                expectedExitCode: 1);
            var root = doc.RootElement;
            Assert.AreEqual("blocked", root.GetProperty("status").GetString());
            AssertStringArrayContains(root.GetProperty("errors"), "promotion package");
            Assert.IsFalse(File.Exists(Path.Combine(workspacePath, ".irondev", "runs", "run-1", "promotion-approval.json")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspacePromotionApproval_InvalidDecision_BlocksEvidence()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-approval-invalid-decision");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            await WritePromotionPackageAsync("run-1", workspacePath, sourceRepo);

            using var doc = await RunWorkspacePromotionApprovalAsync(
                "run-1",
                workspacePath,
                "approve",
                "Rob",
                "Reviewed validation and diff package.",
                expectedExitCode: 1);
            var root = doc.RootElement;
            Assert.AreEqual("blocked", root.GetProperty("status").GetString());
            AssertStringArrayContains(root.GetProperty("errors"), "decision");
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspacePromotionApproval_MissingApprovedBy_BlocksEvidence()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-approval-missing-approvedby");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            await WritePromotionPackageAsync("run-1", workspacePath, sourceRepo);

            using var doc = await RunWorkspacePromotionApprovalAsync(
                "run-1",
                workspacePath,
                "approved",
                string.Empty,
                "Reviewed validation and diff package.",
                expectedExitCode: 1);
            var root = doc.RootElement;
            Assert.AreEqual("blocked", root.GetProperty("status").GetString());
            AssertStringArrayContains(root.GetProperty("errors"), "approved-by");
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspacePromotionApproval_MissingReason_BlocksEvidence()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-approval-missing-reason");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            await WritePromotionPackageAsync("run-1", workspacePath, sourceRepo);

            using var doc = await RunWorkspacePromotionApprovalAsync(
                "run-1",
                workspacePath,
                "approved",
                "Rob",
                string.Empty,
                expectedExitCode: 1);
            var root = doc.RootElement;
            Assert.AreEqual("blocked", root.GetProperty("status").GetString());
            AssertStringArrayContains(root.GetProperty("errors"), "reason");
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspacePromotionApproval_ApprovedDecision_WritesEvidenceButDoesNotAllowApply()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-approval-approved");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            await WritePromotionPackageAsync("run-1", workspacePath, sourceRepo);

            using var doc = await RunWorkspacePromotionApprovalAsync(
                "run-1",
                workspacePath,
                "approved",
                "Rob",
                "Reviewed validation and diff package.",
                expectedExitCode: 0);
            var root = doc.RootElement;
            Assert.AreEqual("succeeded", root.GetProperty("status").GetString());
            Assert.AreEqual("workspace promotion-approval", root.GetProperty("command").GetString());
            Assert.IsFalse(root.TryGetProperty("loopReport", out _));
            Assert.IsFalse(root.TryGetProperty("processRun", out _));

            var data = root.GetProperty("data");
            Assert.AreEqual("approved", data.GetProperty("decision").GetString());
            Assert.IsFalse(data.GetProperty("allowsApply").GetBoolean());
            Assert.IsTrue(data.GetProperty("requiresSeparateApplyCommand").GetBoolean());
            var hash = data.GetProperty("promotionPackageSha256").GetString()!;
            Assert.IsTrue(IsSha256Hex(hash), $"Expected 64-character SHA-256 hex but got '{hash}'.");
            var approvalPath = data.GetProperty("approvalEvidencePath").GetString()!;
            Assert.IsTrue(File.Exists(approvalPath));
            AssertStringArrayContains(data.GetProperty("evidencePaths"), "promotion-package.json");
            AssertStringArrayContains(data.GetProperty("evidencePaths"), "promotion-approval.json");
            Assert.IsFalse(File.Exists(Path.Combine(sourceRepo, ".irondev", "runs", "run-1", "promotion-approval.json")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspacePromotionApproval_RejectedDecision_WritesEvidenceAndDoesNotAllowApply()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-approval-rejected");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            await WritePromotionPackageAsync("run-1", workspacePath, sourceRepo);

            using var doc = await RunWorkspacePromotionApprovalAsync(
                "run-1",
                workspacePath,
                "rejected",
                "Rob",
                "Validation failed; do not promote.",
                expectedExitCode: 0);
            var data = doc.RootElement.GetProperty("data");
            Assert.AreEqual("rejected", data.GetProperty("decision").GetString());
            Assert.IsFalse(data.GetProperty("allowsApply").GetBoolean());
            Assert.IsFalse(data.GetProperty("requiresSeparateApplyCommand").GetBoolean());
            Assert.IsTrue(File.Exists(data.GetProperty("approvalEvidencePath").GetString()));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspacePromotionApproval_HashChangesWhenPromotionPackageChanges()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-approval-hash-binding");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var firstWorkspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            await WritePromotionPackageAsync("run-1", firstWorkspacePath, sourceRepo);

            using var firstDoc = await RunWorkspacePromotionApprovalAsync(
                "run-1",
                firstWorkspacePath,
                "approved",
                "Rob",
                "Reviewed validation and diff package.",
                expectedExitCode: 0);
            var firstHash = firstDoc.RootElement.GetProperty("data").GetProperty("promotionPackageSha256").GetString();

            var secondWorkspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-2", sourceRepo);
            var secondPackagePath = await WritePromotionPackageAsync("run-2", secondWorkspacePath, sourceRepo);
            await File.AppendAllTextAsync(secondPackagePath, Environment.NewLine);

            using var secondDoc = await RunWorkspacePromotionApprovalAsync(
                "run-2",
                secondWorkspacePath,
                "approved",
                "Rob",
                "Reviewed changed promotion package.",
                expectedExitCode: 0);
            var secondHash = secondDoc.RootElement.GetProperty("data").GetProperty("promotionPackageSha256").GetString();

            Assert.IsTrue(IsSha256Hex(firstHash!));
            Assert.IsTrue(IsSha256Hex(secondHash!));
            Assert.AreNotEqual(firstHash, secondHash);
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspacePromotionApproval_ExistingApprovalEvidence_BlocksOverwrite()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-approval-immutable");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            var packagePath = await WritePromotionPackageAsync("run-1", workspacePath, sourceRepo);

            using var firstDoc = await RunWorkspacePromotionApprovalAsync(
                "run-1",
                workspacePath,
                "approved",
                "Rob",
                "Reviewed validation and diff package.",
                expectedExitCode: 0);
            var approvalPath = firstDoc.RootElement.GetProperty("data").GetProperty("approvalEvidencePath").GetString()!;
            var originalEvidence = await File.ReadAllTextAsync(approvalPath);

            await File.AppendAllTextAsync(packagePath, Environment.NewLine);

            using var secondDoc = await RunWorkspacePromotionApprovalAsync(
                "run-1",
                workspacePath,
                "rejected",
                "Rob",
                "Package changed after approval.",
                expectedExitCode: 1);
            var root = secondDoc.RootElement;

            Assert.AreEqual("blocked", root.GetProperty("status").GetString());
            AssertStringArrayContains(root.GetProperty("errors"), "immutable");
            Assert.AreEqual(originalEvidence, await File.ReadAllTextAsync(approvalPath));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspacePromotionApproval_UnsafePromotionPackageFlags_BlockEvidence()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-approval-unsafe-flags");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            await WritePromotionPackageAsync("run-1", workspacePath, sourceRepo, autoPromotionAllowed: true);

            using var doc = await RunWorkspacePromotionApprovalAsync(
                "run-1",
                workspacePath,
                "approved",
                "Rob",
                "Reviewed validation and diff package.",
                expectedExitCode: 1);
            var root = doc.RootElement;
            Assert.AreEqual("blocked", root.GetProperty("status").GetString());
            AssertStringArrayContains(root.GetProperty("errors"), "auto");
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceApplyPreflight_MissingApprovalEvidence_Blocks()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-preflight-missing-approval");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            await WritePromotionPackageAsync("run-1", workspacePath, sourceRepo);
            await WriteDiffEvidenceAsync("run-1", workspacePath, sourceRepo);

            using var doc = await RunWorkspaceApplyPreflightAsync("run-1", workspacePath, expectedExitCode: 1);
            var root = doc.RootElement;

            Assert.AreEqual("blocked", root.GetProperty("status").GetString());
            AssertStringArrayContains(root.GetProperty("errors"), "approval evidence");
            Assert.IsFalse(File.Exists(Path.Combine(workspacePath, ".irondev", "runs", "run-1", "apply-preflight.json")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceApplyPreflight_RejectedApproval_Blocks()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-preflight-rejected");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            await WritePromotionPackageAsync("run-1", workspacePath, sourceRepo);
            await WriteDiffEvidenceAsync("run-1", workspacePath, sourceRepo);
            using var approvalDoc = await RunWorkspacePromotionApprovalAsync("run-1", workspacePath, "rejected", "Rob", "Validation failed.", expectedExitCode: 0);

            using var doc = await RunWorkspaceApplyPreflightAsync("run-1", workspacePath, expectedExitCode: 1);
            var data = doc.RootElement.GetProperty("data");

            Assert.AreEqual("blocked", doc.RootElement.GetProperty("status").GetString());
            Assert.AreEqual("not_ready_rejected", data.GetProperty("recommendation").GetString());
            Assert.IsFalse(data.GetProperty("readyForApply").GetBoolean());
            Assert.IsFalse(data.GetProperty("canApplyNow").GetBoolean());
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceApplyPreflight_PromotionPackageHashMismatch_Blocks()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-preflight-stale-approval");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            var packagePath = await WritePromotionPackageAsync("run-1", workspacePath, sourceRepo);
            await WriteDiffEvidenceAsync("run-1", workspacePath, sourceRepo);
            using var approvalDoc = await RunWorkspacePromotionApprovalAsync("run-1", workspacePath, "approved", "Rob", "Reviewed validation and diff package.", expectedExitCode: 0);
            await File.AppendAllTextAsync(packagePath, Environment.NewLine);

            using var doc = await RunWorkspaceApplyPreflightAsync("run-1", workspacePath, expectedExitCode: 1);
            var data = doc.RootElement.GetProperty("data");

            Assert.AreEqual("blocked", doc.RootElement.GetProperty("status").GetString());
            Assert.AreEqual("not_ready_stale_approval", data.GetProperty("recommendation").GetString());
            Assert.IsFalse(data.GetProperty("promotionPackageHashMatchesApproval").GetBoolean());
            Assert.IsFalse(data.GetProperty("readyForApply").GetBoolean());
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceApplyPreflight_UnsafeApprovalFlags_Block()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-preflight-unsafe-approval");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            var packagePath = await WritePromotionPackageAsync("run-1", workspacePath, sourceRepo);
            await WriteDiffEvidenceAsync("run-1", workspacePath, sourceRepo);
            await WriteApprovalEvidenceAsync("run-1", workspacePath, packagePath, decision: "approved", allowsApply: true, requiresSeparateApplyCommand: true);

            using var doc = await RunWorkspaceApplyPreflightAsync("run-1", workspacePath, expectedExitCode: 1);

            Assert.AreEqual("blocked", doc.RootElement.GetProperty("status").GetString());
            AssertStringArrayContains(doc.RootElement.GetProperty("errors"), "allowsApply");
            Assert.IsFalse(File.Exists(Path.Combine(workspacePath, ".irondev", "runs", "run-1", "apply-preflight.json")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceApplyPreflight_ApprovalMissingApprovedBy_Blocks()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-preflight-missing-approved-by");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            var packagePath = await WritePromotionPackageAsync("run-1", workspacePath, sourceRepo);
            await WriteDiffEvidenceAsync("run-1", workspacePath, sourceRepo);
            await WriteApprovalEvidenceAsync("run-1", workspacePath, packagePath, decision: "approved", allowsApply: false, requiresSeparateApplyCommand: true, includeApprovedBy: false);

            using var doc = await RunWorkspaceApplyPreflightAsync("run-1", workspacePath, expectedExitCode: 1);
            var root = doc.RootElement;
            var data = root.GetProperty("data");

            Assert.AreEqual("blocked", root.GetProperty("status").GetString());
            AssertStringArrayContains(root.GetProperty("errors"), "approvedBy");
            Assert.IsFalse(data.GetProperty("readyForApply").GetBoolean());
            Assert.IsFalse(data.GetProperty("canApplyNow").GetBoolean());
            Assert.IsFalse(File.Exists(Path.Combine(workspacePath, ".irondev", "runs", "run-1", "apply-preflight.json")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceApplyPreflight_ApprovalMissingReason_Blocks()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-preflight-missing-reason");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            var packagePath = await WritePromotionPackageAsync("run-1", workspacePath, sourceRepo);
            await WriteDiffEvidenceAsync("run-1", workspacePath, sourceRepo);
            await WriteApprovalEvidenceAsync("run-1", workspacePath, packagePath, decision: "approved", allowsApply: false, requiresSeparateApplyCommand: true, includeReason: false);

            using var doc = await RunWorkspaceApplyPreflightAsync("run-1", workspacePath, expectedExitCode: 1);
            var root = doc.RootElement;
            var data = root.GetProperty("data");

            Assert.AreEqual("blocked", root.GetProperty("status").GetString());
            AssertStringArrayContains(root.GetProperty("errors"), "reason");
            Assert.IsFalse(data.GetProperty("readyForApply").GetBoolean());
            Assert.IsFalse(data.GetProperty("canApplyNow").GetBoolean());
            Assert.IsFalse(File.Exists(Path.Combine(workspacePath, ".irondev", "runs", "run-1", "apply-preflight.json")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceApplyPreflight_ApprovalMissingPromotionPackagePath_Blocks()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-preflight-missing-package-path");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            var packagePath = await WritePromotionPackageAsync("run-1", workspacePath, sourceRepo);
            await WriteDiffEvidenceAsync("run-1", workspacePath, sourceRepo);
            await WriteApprovalEvidenceAsync("run-1", workspacePath, packagePath, decision: "approved", allowsApply: false, requiresSeparateApplyCommand: true, includePromotionPackagePath: false);

            using var doc = await RunWorkspaceApplyPreflightAsync("run-1", workspacePath, expectedExitCode: 1);
            var root = doc.RootElement;
            var data = root.GetProperty("data");

            Assert.AreEqual("blocked", root.GetProperty("status").GetString());
            AssertStringArrayContains(root.GetProperty("errors"), "promotionPackagePath");
            Assert.IsFalse(data.GetProperty("readyForApply").GetBoolean());
            Assert.IsFalse(data.GetProperty("canApplyNow").GetBoolean());
            Assert.IsFalse(File.Exists(Path.Combine(workspacePath, ".irondev", "runs", "run-1", "apply-preflight.json")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceApplyPreflight_PromotionPackageMissingSourceRepo_Blocks()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-preflight-package-missing-source");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            var packagePath = await WritePromotionPackageAsync("run-1", workspacePath, sourceRepo, includeSourceRepo: false);
            await WriteDiffEvidenceAsync("run-1", workspacePath, sourceRepo);
            await WriteApprovalEvidenceAsync("run-1", workspacePath, packagePath, decision: "approved", allowsApply: false, requiresSeparateApplyCommand: true);

            using var doc = await RunWorkspaceApplyPreflightAsync("run-1", workspacePath, expectedExitCode: 1);
            var root = doc.RootElement;
            var data = root.GetProperty("data");

            Assert.AreEqual("blocked", root.GetProperty("status").GetString());
            AssertStringArrayContains(root.GetProperty("errors"), "Promotion package is missing sourceRepo.");
            Assert.IsFalse(data.GetProperty("readyForApply").GetBoolean());
            Assert.IsFalse(data.GetProperty("canApplyNow").GetBoolean());
            Assert.IsFalse(File.Exists(Path.Combine(workspacePath, ".irondev", "runs", "run-1", "apply-preflight.json")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceApplyPreflight_DiffEvidenceMissingSourceRepo_Blocks()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-preflight-diff-missing-source");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            var packagePath = await WritePromotionPackageAsync("run-1", workspacePath, sourceRepo);
            await WriteDiffEvidenceAsync("run-1", workspacePath, sourceRepo, includeSourceRepo: false);
            await WriteApprovalEvidenceAsync("run-1", workspacePath, packagePath, decision: "approved", allowsApply: false, requiresSeparateApplyCommand: true);

            using var doc = await RunWorkspaceApplyPreflightAsync("run-1", workspacePath, expectedExitCode: 1);
            var root = doc.RootElement;
            var data = root.GetProperty("data");

            Assert.AreEqual("blocked", root.GetProperty("status").GetString());
            AssertStringArrayContains(root.GetProperty("errors"), "Diff evidence is missing sourceRepo.");
            Assert.IsFalse(data.GetProperty("readyForApply").GetBoolean());
            Assert.IsFalse(data.GetProperty("canApplyNow").GetBoolean());
            Assert.IsFalse(File.Exists(Path.Combine(workspacePath, ".irondev", "runs", "run-1", "apply-preflight.json")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceApplyPreflight_UnsafePromotionPackageFlags_Block()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-preflight-unsafe-package");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            var packagePath = await WritePromotionPackageAsync("run-1", workspacePath, sourceRepo, autoPromotionAllowed: true);
            await WriteDiffEvidenceAsync("run-1", workspacePath, sourceRepo);
            await WriteApprovalEvidenceAsync("run-1", workspacePath, packagePath, decision: "approved", allowsApply: false, requiresSeparateApplyCommand: true);

            using var doc = await RunWorkspaceApplyPreflightAsync("run-1", workspacePath, expectedExitCode: 1);

            Assert.AreEqual("blocked", doc.RootElement.GetProperty("status").GetString());
            AssertStringArrayContains(doc.RootElement.GetProperty("errors"), "automatic promotion");
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceApplyPreflight_ValidationFailed_Blocks()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-preflight-validation-failed");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            var packagePath = await WritePromotionPackageAsync("run-1", workspacePath, sourceRepo, validationSucceeded: false, recommendation: "not_ready_validation_failed");
            await WriteDiffEvidenceAsync("run-1", workspacePath, sourceRepo);
            await WriteApprovalEvidenceAsync("run-1", workspacePath, packagePath, decision: "approved", allowsApply: false, requiresSeparateApplyCommand: true);

            using var doc = await RunWorkspaceApplyPreflightAsync("run-1", workspacePath, expectedExitCode: 1);
            var data = doc.RootElement.GetProperty("data");

            Assert.AreEqual("blocked", doc.RootElement.GetProperty("status").GetString());
            Assert.AreEqual("not_ready_validation_failed", data.GetProperty("recommendation").GetString());
            Assert.IsFalse(data.GetProperty("readyForApply").GetBoolean());
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceApplyPreflight_NoChanges_Blocks()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-preflight-no-changes");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            var packagePath = await WritePromotionPackageAsync("run-1", workspacePath, sourceRepo);
            await WriteDiffEvidenceAsync("run-1", workspacePath, sourceRepo, changed: false, modifiedFiles: []);
            await WriteApprovalEvidenceAsync("run-1", workspacePath, packagePath, decision: "approved", allowsApply: false, requiresSeparateApplyCommand: true);

            using var doc = await RunWorkspaceApplyPreflightAsync("run-1", workspacePath, expectedExitCode: 1);
            var data = doc.RootElement.GetProperty("data");

            Assert.AreEqual("blocked", doc.RootElement.GetProperty("status").GetString());
            Assert.AreEqual("not_ready_no_changes", data.GetProperty("recommendation").GetString());
            Assert.IsFalse(data.GetProperty("readyForApply").GetBoolean());
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceApplyPreflight_CoherentApprovedPackage_WritesPreflightArtifact()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-preflight-ready");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            await WritePromotionPackageAsync("run-1", workspacePath, sourceRepo);
            await WriteDiffEvidenceAsync("run-1", workspacePath, sourceRepo);
            using var approvalDoc = await RunWorkspacePromotionApprovalAsync("run-1", workspacePath, "approved", "Rob", "Reviewed validation and diff package.", expectedExitCode: 0);

            using var doc = await RunWorkspaceApplyPreflightAsync("run-1", workspacePath, expectedExitCode: 0);
            var root = doc.RootElement;
            var data = root.GetProperty("data");

            Assert.AreEqual("succeeded", root.GetProperty("status").GetString());
            Assert.AreEqual("workspace apply-preflight", root.GetProperty("command").GetString());
            Assert.IsTrue(root.TryGetProperty("traceId", out _));
            Assert.IsTrue(root.TryGetProperty("summary", out _));
            Assert.IsTrue(root.TryGetProperty("errors", out _));
            Assert.IsTrue(root.TryGetProperty("warnings", out _));
            Assert.IsFalse(root.TryGetProperty("loopReport", out _));
            Assert.IsFalse(root.TryGetProperty("processRun", out _));
            Assert.AreEqual("ready_for_separate_apply_command", data.GetProperty("recommendation").GetString());
            Assert.IsTrue(data.GetProperty("readyForApply").GetBoolean());
            Assert.IsFalse(data.GetProperty("canApplyNow").GetBoolean());
            Assert.IsTrue(data.GetProperty("requiresSeparateApplyCommand").GetBoolean());
            Assert.IsTrue(data.GetProperty("promotionPackageHashMatchesApproval").GetBoolean());
            AssertStringArrayContains(data.GetProperty("evidencePaths"), "workspace.json");
            AssertStringArrayContains(data.GetProperty("evidencePaths"), "promotion-package.json");
            AssertStringArrayContains(data.GetProperty("evidencePaths"), "promotion-approval.json");
            AssertStringArrayContains(data.GetProperty("evidencePaths"), "diff.json");
            AssertStringArrayContains(data.GetProperty("evidencePaths"), "apply-preflight.json");
            Assert.IsTrue(File.Exists(data.GetProperty("applyPreflightPath").GetString()));
            Assert.IsFalse(Directory.Exists(Path.Combine(sourceRepo, ".irondev")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceApplyDryRun_MissingRequiredOptions_WithJson_ReturnsFailureEnvelope()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var result = await IronDevCli.RunAsync(
            ["workspace", "apply-dry-run", "--json"],
            output,
            error,
            handler: null,
            CancellationToken.None);

        Assert.AreEqual(2, result, error.ToString());
        AssertJsonWasWritten(output);

        using var doc = JsonDocument.Parse(output.ToString());
        var root = doc.RootElement;
        Assert.AreEqual("failed", root.GetProperty("status").GetString());
        Assert.AreEqual("workspace apply-dry-run", root.GetProperty("command").GetString());
        Assert.AreEqual(JsonValueKind.Object, root.GetProperty("data").ValueKind);
        AssertArrayNotEmpty(root.GetProperty("errors"));
    }

    [TestMethod]
    public async Task WorkspaceApplyDryRun_MissingApplyPreflight_Blocks()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-apply-dry-run-missing-preflight");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            await File.WriteAllTextAsync(Path.Combine(workspacePath, "README.md"), "workspace change");
            var packagePath = await WritePromotionPackageAsync("run-1", workspacePath, sourceRepo);
            await WriteDiffEvidenceAsync("run-1", workspacePath, sourceRepo, modifiedFiles: ["README.md"]);
            await WriteApprovalEvidenceAsync("run-1", workspacePath, packagePath, decision: "approved", allowsApply: false, requiresSeparateApplyCommand: true);

            using var doc = await RunWorkspaceApplyDryRunAsync("run-1", workspacePath, expectedExitCode: 1);
            var root = doc.RootElement;

            Assert.AreEqual("blocked", root.GetProperty("status").GetString());
            AssertArrayNotEmpty(root.GetProperty("errors"));
            Assert.IsFalse(File.Exists(Path.Combine(workspacePath, ".irondev", "runs", "run-1", "apply-dry-run.json")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceApplyDryRun_PreflightNotReady_Blocks()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-apply-dry-run-preflight-not-ready");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            await File.WriteAllTextAsync(Path.Combine(workspacePath, "README.md"), "workspace change");
            var packagePath = await WritePromotionPackageAsync("run-1", workspacePath, sourceRepo);
            await WriteDiffEvidenceAsync("run-1", workspacePath, sourceRepo, modifiedFiles: ["README.md"]);
            await WriteApprovalEvidenceAsync("run-1", workspacePath, packagePath, decision: "approved", allowsApply: false, requiresSeparateApplyCommand: true);
            await WriteApplyPreflightEvidenceAsync("run-1", workspacePath, sourceRepo, readyForApply: false, recommendation: "not_ready_validation_failed");

            using var doc = await RunWorkspaceApplyDryRunAsync("run-1", workspacePath, expectedExitCode: 1);
            var root = doc.RootElement;

            Assert.AreEqual("blocked", root.GetProperty("status").GetString());
            Assert.IsFalse(root.GetProperty("data").GetProperty("readyForApply").GetBoolean());
            AssertArrayNotEmpty(root.GetProperty("errors"));
            Assert.IsFalse(File.Exists(Path.Combine(workspacePath, ".irondev", "runs", "run-1", "apply-dry-run.json")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceApplyDryRun_PreflightCanApplyNow_Blocks()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-apply-dry-run-can-apply-now");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            await File.WriteAllTextAsync(Path.Combine(workspacePath, "README.md"), "workspace change");
            var packagePath = await WritePromotionPackageAsync("run-1", workspacePath, sourceRepo);
            await WriteDiffEvidenceAsync("run-1", workspacePath, sourceRepo, modifiedFiles: ["README.md"]);
            await WriteApprovalEvidenceAsync("run-1", workspacePath, packagePath, decision: "approved", allowsApply: false, requiresSeparateApplyCommand: true);
            await WriteApplyPreflightEvidenceAsync("run-1", workspacePath, sourceRepo, canApplyNow: true);

            using var doc = await RunWorkspaceApplyDryRunAsync("run-1", workspacePath, expectedExitCode: 1);
            var root = doc.RootElement;

            Assert.AreEqual("blocked", root.GetProperty("status").GetString());
            Assert.IsTrue(root.GetProperty("data").GetProperty("canApplyNow").GetBoolean());
            AssertArrayNotEmpty(root.GetProperty("errors"));
            Assert.IsFalse(File.Exists(Path.Combine(workspacePath, ".irondev", "runs", "run-1", "apply-dry-run.json")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceApplyDryRun_UnsafeDiffPath_Blocks()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-apply-dry-run-unsafe-path");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            var packagePath = await WritePromotionPackageAsync("run-1", workspacePath, sourceRepo);
            await WriteDiffEvidenceAsync("run-1", workspacePath, sourceRepo, addedFiles: [Path.Combine(Path.GetTempPath(), "outside.txt"), "../outside.txt", ".git/config", ".irondev/runs/escape.txt"], modifiedFiles: []);
            await WriteApprovalEvidenceAsync("run-1", workspacePath, packagePath, decision: "approved", allowsApply: false, requiresSeparateApplyCommand: true);
            await WriteApplyPreflightEvidenceAsync("run-1", workspacePath, sourceRepo);

            using var doc = await RunWorkspaceApplyDryRunAsync("run-1", workspacePath, expectedExitCode: 1);
            var root = doc.RootElement;

            Assert.AreEqual("blocked", root.GetProperty("status").GetString());
            AssertArrayNotEmpty(root.GetProperty("errors"));
            Assert.IsFalse(File.Exists(Path.Combine(workspacePath, ".irondev", "runs", "run-1", "apply-dry-run.json")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceApplyDryRun_AddModifyDeleteOperations_WritesPlanArtifact()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-apply-dry-run-operations");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            await File.WriteAllTextAsync(Path.Combine(sourceRepo, "tracked.txt"), "source tracked file");
            await File.WriteAllTextAsync(Path.Combine(sourceRepo, "old.txt"), "source old file");
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            var sourceTrackedBefore = await File.ReadAllTextAsync(Path.Combine(sourceRepo, "tracked.txt"));
            await File.WriteAllTextAsync(Path.Combine(workspacePath, "tracked.txt"), "workspace modified tracked file");
            await File.WriteAllTextAsync(Path.Combine(workspacePath, "new.txt"), "workspace new file");
            File.Delete(Path.Combine(workspacePath, "old.txt"));

            var packagePath = await WritePromotionPackageAsync("run-1", workspacePath, sourceRepo);
            await WriteDiffEvidenceAsync(
                "run-1",
                workspacePath,
                sourceRepo,
                addedFiles: ["new.txt"],
                modifiedFiles: ["tracked.txt"],
                deletedFiles: ["old.txt"]);
            await WriteApprovalEvidenceAsync("run-1", workspacePath, packagePath, decision: "approved", allowsApply: false, requiresSeparateApplyCommand: true);
            await WriteApplyPreflightEvidenceAsync("run-1", workspacePath, sourceRepo);

            using var doc = await RunWorkspaceApplyDryRunAsync("run-1", workspacePath, expectedExitCode: 0);
            var root = doc.RootElement;
            var data = root.GetProperty("data");
            var operations = data.GetProperty("operations").EnumerateArray().ToArray();

            Assert.AreEqual("succeeded", root.GetProperty("status").GetString());
            Assert.AreEqual("workspace apply-dry-run", root.GetProperty("command").GetString());
            Assert.IsFalse(root.TryGetProperty("loopReport", out _));
            Assert.IsFalse(root.TryGetProperty("processRun", out _));
            Assert.IsTrue(data.GetProperty("readyForApply").GetBoolean());
            Assert.IsFalse(data.GetProperty("canApplyNow").GetBoolean());
            Assert.IsTrue(data.GetProperty("requiresSeparateApplyCommand").GetBoolean());
            Assert.AreEqual("ready_for_separate_apply_command", data.GetProperty("recommendation").GetString());
            Assert.AreEqual(1, data.GetProperty("addCount").GetInt32());
            Assert.AreEqual(1, data.GetProperty("modifyCount").GetInt32());
            Assert.AreEqual(1, data.GetProperty("deleteCount").GetInt32());
            Assert.AreEqual(3, operations.Length);
            Assert.IsTrue(operations.Any(operation =>
                operation.GetProperty("operation").GetString() == "add" &&
                operation.GetProperty("relativePath").GetString() == "new.txt"));
            Assert.IsTrue(operations.Any(operation =>
                operation.GetProperty("operation").GetString() == "modify" &&
                operation.GetProperty("relativePath").GetString() == "tracked.txt"));
            Assert.IsTrue(operations.Any(operation =>
                operation.GetProperty("operation").GetString() == "delete" &&
                operation.GetProperty("relativePath").GetString() == "old.txt"));
            AssertStringArrayContains(data.GetProperty("evidencePaths"), "workspace.json");
            AssertStringArrayContains(data.GetProperty("evidencePaths"), "diff.json");
            AssertStringArrayContains(data.GetProperty("evidencePaths"), "promotion-package.json");
            AssertStringArrayContains(data.GetProperty("evidencePaths"), "promotion-approval.json");
            AssertStringArrayContains(data.GetProperty("evidencePaths"), "apply-preflight.json");
            AssertStringArrayContains(data.GetProperty("evidencePaths"), "apply-dry-run.json");
            Assert.IsTrue(File.Exists(data.GetProperty("applyDryRunPath").GetString()));
            Assert.AreEqual(sourceTrackedBefore, await File.ReadAllTextAsync(Path.Combine(sourceRepo, "tracked.txt")));
            Assert.IsFalse(Directory.Exists(Path.Combine(sourceRepo, ".irondev")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceApplyDryRun_OperationMismatch_Blocks()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-apply-dry-run-mismatch");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            var packagePath = await WritePromotionPackageAsync("run-1", workspacePath, sourceRepo);
            await WriteDiffEvidenceAsync("run-1", workspacePath, sourceRepo, addedFiles: ["missing.txt"], modifiedFiles: []);
            await WriteApprovalEvidenceAsync("run-1", workspacePath, packagePath, decision: "approved", allowsApply: false, requiresSeparateApplyCommand: true);
            await WriteApplyPreflightEvidenceAsync("run-1", workspacePath, sourceRepo);

            using var doc = await RunWorkspaceApplyDryRunAsync("run-1", workspacePath, expectedExitCode: 1);
            var root = doc.RootElement;

            Assert.AreEqual("blocked", root.GetProperty("status").GetString());
            AssertArrayNotEmpty(root.GetProperty("errors"));
            Assert.IsFalse(File.Exists(Path.Combine(workspacePath, ".irondev", "runs", "run-1", "apply-dry-run.json")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceApplyDryRun_AddOperation_SourceDirectoryConflict_Blocks()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-apply-dry-run-directory-conflict");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            Directory.CreateDirectory(Path.Combine(sourceRepo, "conflict"));
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            if (Directory.Exists(Path.Combine(workspacePath, "conflict")))
                Directory.Delete(Path.Combine(workspacePath, "conflict"), recursive: true);
            await File.WriteAllTextAsync(Path.Combine(workspacePath, "conflict"), "workspace file");
            var sourceConflictStillDirectory = Directory.Exists(Path.Combine(sourceRepo, "conflict"));

            var packagePath = await WritePromotionPackageAsync("run-1", workspacePath, sourceRepo);
            await WriteDiffEvidenceAsync("run-1", workspacePath, sourceRepo, addedFiles: ["conflict"], modifiedFiles: []);
            await WriteApprovalEvidenceAsync("run-1", workspacePath, packagePath, decision: "approved", allowsApply: false, requiresSeparateApplyCommand: true);
            await WriteApplyPreflightEvidenceAsync("run-1", workspacePath, sourceRepo);

            using var doc = await RunWorkspaceApplyDryRunAsync("run-1", workspacePath, expectedExitCode: 1);
            var root = doc.RootElement;

            Assert.AreEqual("blocked", root.GetProperty("status").GetString());
            AssertStringArrayContains(root.GetProperty("errors"), "source directory");
            Assert.IsFalse(File.Exists(Path.Combine(workspacePath, ".irondev", "runs", "run-1", "apply-dry-run.json")));
            Assert.IsTrue(sourceConflictStillDirectory);
            Assert.IsTrue(Directory.Exists(Path.Combine(sourceRepo, "conflict")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceApplyDryRun_ConflictingDiffOperations_Block()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-apply-dry-run-conflicting-diff");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            await File.WriteAllTextAsync(Path.Combine(workspacePath, "conflict.txt"), "workspace file");

            var packagePath = await WritePromotionPackageAsync("run-1", workspacePath, sourceRepo);
            await WriteDiffEvidenceAsync("run-1", workspacePath, sourceRepo, addedFiles: ["conflict.txt", "duplicate.txt", "duplicate.txt"], modifiedFiles: ["conflict.txt"]);
            await WriteApprovalEvidenceAsync("run-1", workspacePath, packagePath, decision: "approved", allowsApply: false, requiresSeparateApplyCommand: true);
            await WriteApplyPreflightEvidenceAsync("run-1", workspacePath, sourceRepo);

            using var doc = await RunWorkspaceApplyDryRunAsync("run-1", workspacePath, expectedExitCode: 1);
            var root = doc.RootElement;

            Assert.AreEqual("blocked", root.GetProperty("status").GetString());
            AssertStringArrayContains(root.GetProperty("errors"), "conflicting operations");
            AssertStringArrayContains(root.GetProperty("errors"), "duplicate add operation");
            Assert.IsFalse(File.Exists(Path.Combine(workspacePath, ".irondev", "runs", "run-1", "apply-dry-run.json")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceApplyCopy_MissingApplyDryRun_Blocks()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-apply-copy-missing-dry-run");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            await File.WriteAllTextAsync(Path.Combine(sourceRepo, "tracked.txt"), "source");
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            await File.WriteAllTextAsync(Path.Combine(workspacePath, "tracked.txt"), "workspace");
            var packagePath = await WritePromotionPackageAsync("run-1", workspacePath, sourceRepo);
            await WriteDiffEvidenceAsync("run-1", workspacePath, sourceRepo, modifiedFiles: ["tracked.txt"]);
            await WriteApprovalEvidenceAsync("run-1", workspacePath, packagePath, decision: "approved", allowsApply: false, requiresSeparateApplyCommand: true);
            await WriteApplyPreflightEvidenceAsync("run-1", workspacePath, sourceRepo);

            using var doc = await RunWorkspaceApplyCopyAsync("run-1", workspacePath, expectedExitCode: 1);

            Assert.AreEqual("blocked", doc.RootElement.GetProperty("status").GetString());
            Assert.AreEqual("source", await File.ReadAllTextAsync(Path.Combine(sourceRepo, "tracked.txt")));
            Assert.IsFalse(File.Exists(Path.Combine(workspacePath, ".irondev", "runs", "run-1", "apply-copy.json")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceApplyCopy_DryRunNotReady_Blocks()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-apply-copy-not-ready");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            await File.WriteAllTextAsync(Path.Combine(sourceRepo, "tracked.txt"), "source");
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            await File.WriteAllTextAsync(Path.Combine(workspacePath, "tracked.txt"), "workspace");
            await WriteApplyCopyRequiredEvidenceAsync("run-1", workspacePath, sourceRepo, [("modify", "tracked.txt")], dryRunReadyForApply: false);

            using var doc = await RunWorkspaceApplyCopyAsync("run-1", workspacePath, expectedExitCode: 1);

            Assert.AreEqual("blocked", doc.RootElement.GetProperty("status").GetString());
            Assert.AreEqual("source", await File.ReadAllTextAsync(Path.Combine(sourceRepo, "tracked.txt")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceApplyCopy_DeleteOperation_Blocks()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-apply-copy-delete");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            await File.WriteAllTextAsync(Path.Combine(sourceRepo, "old.txt"), "source old");
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            File.Delete(Path.Combine(workspacePath, "old.txt"));
            await WriteApplyCopyRequiredEvidenceAsync("run-1", workspacePath, sourceRepo, [("delete", "old.txt")]);

            using var doc = await RunWorkspaceApplyCopyAsync("run-1", workspacePath, expectedExitCode: 1);

            Assert.AreEqual("blocked", doc.RootElement.GetProperty("status").GetString());
            AssertStringArrayContains(doc.RootElement.GetProperty("errors"), "Delete operations are not supported");
            Assert.AreEqual("source old", await File.ReadAllTextAsync(Path.Combine(sourceRepo, "old.txt")));
            Assert.IsFalse(File.Exists(Path.Combine(workspacePath, ".irondev", "runs", "run-1", "apply-copy.json")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceApplyCopy_SourceHashDrift_Blocks()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-apply-copy-source-drift");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            await File.WriteAllTextAsync(Path.Combine(sourceRepo, "tracked.txt"), "source");
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            await File.WriteAllTextAsync(Path.Combine(workspacePath, "tracked.txt"), "workspace");
            await WriteApplyCopyRequiredEvidenceAsync("run-1", workspacePath, sourceRepo, [("modify", "tracked.txt")]);
            await File.WriteAllTextAsync(Path.Combine(sourceRepo, "tracked.txt"), "drift");

            using var doc = await RunWorkspaceApplyCopyAsync("run-1", workspacePath, expectedExitCode: 1);

            Assert.AreEqual("blocked", doc.RootElement.GetProperty("status").GetString());
            AssertStringArrayContains(doc.RootElement.GetProperty("errors"), "Source hash mismatch");
            Assert.AreEqual("drift", await File.ReadAllTextAsync(Path.Combine(sourceRepo, "tracked.txt")));
            Assert.IsFalse(File.Exists(Path.Combine(workspacePath, ".irondev", "runs", "run-1", "apply-copy.json")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceApplyCopy_WorkspaceHashDrift_Blocks()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-apply-copy-workspace-drift");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            await File.WriteAllTextAsync(Path.Combine(sourceRepo, "tracked.txt"), "source");
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            await File.WriteAllTextAsync(Path.Combine(workspacePath, "tracked.txt"), "workspace");
            await WriteApplyCopyRequiredEvidenceAsync("run-1", workspacePath, sourceRepo, [("modify", "tracked.txt")]);
            await File.WriteAllTextAsync(Path.Combine(workspacePath, "tracked.txt"), "workspace drift");

            using var doc = await RunWorkspaceApplyCopyAsync("run-1", workspacePath, expectedExitCode: 1);

            Assert.AreEqual("blocked", doc.RootElement.GetProperty("status").GetString());
            AssertStringArrayContains(doc.RootElement.GetProperty("errors"), "Workspace hash mismatch");
            Assert.AreEqual("source", await File.ReadAllTextAsync(Path.Combine(sourceRepo, "tracked.txt")));
            Assert.IsFalse(File.Exists(Path.Combine(workspacePath, ".irondev", "runs", "run-1", "apply-copy.json")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceApplyCopy_AddOperation_AppliesFile()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-apply-copy-add");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            await File.WriteAllTextAsync(Path.Combine(workspacePath, "new.txt"), "new workspace file");
            await WriteApplyCopyRequiredEvidenceAsync("run-1", workspacePath, sourceRepo, [("add", "new.txt")]);

            using var doc = await RunWorkspaceApplyCopyAsync("run-1", workspacePath, expectedExitCode: 0);
            var data = doc.RootElement.GetProperty("data");

            Assert.AreEqual("succeeded", doc.RootElement.GetProperty("status").GetString());
            Assert.IsTrue(File.Exists(Path.Combine(sourceRepo, "new.txt")));
            Assert.AreEqual("new workspace file", await File.ReadAllTextAsync(Path.Combine(sourceRepo, "new.txt")));
            Assert.IsTrue(data.GetProperty("sourceRepoMutated").GetBoolean());
            Assert.IsTrue(File.Exists(data.GetProperty("applyCopyPath").GetString()));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceApplyCopy_NestedAddOperation_CreatesParentDirectoryAndAppliesFile()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-apply-copy-nested-add");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            Directory.CreateDirectory(Path.Combine(workspacePath, "src"));
            await File.WriteAllTextAsync(Path.Combine(workspacePath, "src", "new.cs"), "namespace Demo;");
            await WriteApplyCopyRequiredEvidenceAsync("run-1", workspacePath, sourceRepo, [("add", "src/new.cs")]);

            using var doc = await RunWorkspaceApplyCopyAsync("run-1", workspacePath, expectedExitCode: 0);

            Assert.AreEqual("succeeded", doc.RootElement.GetProperty("status").GetString());
            Assert.IsTrue(File.Exists(Path.Combine(sourceRepo, "src", "new.cs")));
            Assert.AreEqual(
                await ComputeSha256Async(Path.Combine(workspacePath, "src", "new.cs")),
                await ComputeSha256Async(Path.Combine(sourceRepo, "src", "new.cs")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceApplyCopy_ModifyOperation_AppliesFile()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-apply-copy-modify");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            await File.WriteAllTextAsync(Path.Combine(sourceRepo, "tracked.txt"), "source");
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            await File.WriteAllTextAsync(Path.Combine(workspacePath, "tracked.txt"), "workspace");
            await WriteApplyCopyRequiredEvidenceAsync("run-1", workspacePath, sourceRepo, [("modify", "tracked.txt")]);

            using var doc = await RunWorkspaceApplyCopyAsync("run-1", workspacePath, expectedExitCode: 0);
            var data = doc.RootElement.GetProperty("data");
            var operations = data.GetProperty("operations").EnumerateArray().ToArray();

            Assert.AreEqual("succeeded", doc.RootElement.GetProperty("status").GetString());
            Assert.AreEqual("workspace", await File.ReadAllTextAsync(Path.Combine(sourceRepo, "tracked.txt")));
            Assert.AreEqual(await ComputeSha256Async(Path.Combine(workspacePath, "tracked.txt")), await ComputeSha256Async(Path.Combine(sourceRepo, "tracked.txt")));
            Assert.IsTrue(data.GetProperty("sourceRepoMutated").GetBoolean());
            Assert.IsTrue(operations.All(operation => operation.GetProperty("applied").GetBoolean()));
            Assert.IsTrue(File.Exists(data.GetProperty("applyCopyPath").GetString()));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceApplyCopy_AddAndModify_AppliesBoth()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-apply-copy-add-modify");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            await File.WriteAllTextAsync(Path.Combine(sourceRepo, "tracked.txt"), "source");
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            await File.WriteAllTextAsync(Path.Combine(workspacePath, "tracked.txt"), "workspace");
            await File.WriteAllTextAsync(Path.Combine(workspacePath, "new.txt"), "new workspace file");
            await WriteApplyCopyRequiredEvidenceAsync("run-1", workspacePath, sourceRepo, [("add", "new.txt"), ("modify", "tracked.txt")]);

            using var doc = await RunWorkspaceApplyCopyAsync("run-1", workspacePath, expectedExitCode: 0);
            var root = doc.RootElement;
            var data = root.GetProperty("data");
            var expectedTopLevelKeys = new[] { "status", "command", "traceId", "summary", "data", "errors", "warnings" };

            CollectionAssert.AreEquivalent(expectedTopLevelKeys, root.EnumerateObject().Select(property => property.Name).ToArray());
            Assert.AreEqual("succeeded", root.GetProperty("status").GetString());
            Assert.AreEqual("workspace apply-copy", root.GetProperty("command").GetString());
            Assert.IsFalse(root.TryGetProperty("loopReport", out _));
            Assert.IsFalse(root.TryGetProperty("processRun", out _));
            Assert.AreEqual(1, data.GetProperty("addCount").GetInt32());
            Assert.AreEqual(1, data.GetProperty("modifyCount").GetInt32());
            Assert.AreEqual(0, data.GetProperty("deleteCount").GetInt32());
            Assert.AreEqual("new workspace file", await File.ReadAllTextAsync(Path.Combine(sourceRepo, "new.txt")));
            Assert.AreEqual("workspace", await File.ReadAllTextAsync(Path.Combine(sourceRepo, "tracked.txt")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceApplyCopy_ValidationIsAllOrNothingBeforeCopy()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-apply-copy-all-or-nothing");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            await File.WriteAllTextAsync(Path.Combine(sourceRepo, "tracked.txt"), "source");
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            await File.WriteAllTextAsync(Path.Combine(workspacePath, "tracked.txt"), "workspace");
            await File.WriteAllTextAsync(Path.Combine(workspacePath, "new.txt"), "new workspace file");
            await WriteApplyCopyRequiredEvidenceAsync("run-1", workspacePath, sourceRepo, [("add", "new.txt"), ("modify", "tracked.txt")]);
            await File.WriteAllTextAsync(Path.Combine(sourceRepo, "tracked.txt"), "drift");

            using var doc = await RunWorkspaceApplyCopyAsync("run-1", workspacePath, expectedExitCode: 1);

            Assert.AreEqual("blocked", doc.RootElement.GetProperty("status").GetString());
            Assert.IsFalse(File.Exists(Path.Combine(sourceRepo, "new.txt")));
            Assert.AreEqual("drift", await File.ReadAllTextAsync(Path.Combine(sourceRepo, "tracked.txt")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceApplyCopy_ParentPathBlockedByFile_BlocksBeforeCopy()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-apply-copy-parent-file");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            await File.WriteAllTextAsync(Path.Combine(sourceRepo, "src"), "source path blocker");
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            await File.WriteAllTextAsync(Path.Combine(workspacePath, "good-target"), "valid add");
            if (File.Exists(Path.Combine(workspacePath, "src")))
                File.Delete(Path.Combine(workspacePath, "src"));
            Directory.CreateDirectory(Path.Combine(workspacePath, "src"));
            await File.WriteAllTextAsync(Path.Combine(workspacePath, "src", "new.cs"), "namespace Demo;");
            await WriteApplyCopyRequiredEvidenceAsync("run-1", workspacePath, sourceRepo, [("add", "good-target"), ("add", "src/new.cs")]);

            using var doc = await RunWorkspaceApplyCopyAsync("run-1", workspacePath, expectedExitCode: 1);

            Assert.AreEqual("blocked", doc.RootElement.GetProperty("status").GetString());
            AssertStringArrayContains(doc.RootElement.GetProperty("errors"), "parent path is blocked by an existing file");
            Assert.IsFalse(File.Exists(Path.Combine(sourceRepo, "good-target")));
            Assert.IsTrue(File.Exists(Path.Combine(sourceRepo, "src")));
            Assert.AreEqual("source path blocker", await File.ReadAllTextAsync(Path.Combine(sourceRepo, "src")));
            Assert.IsFalse(File.Exists(Path.Combine(workspacePath, ".irondev", "runs", "run-1", "apply-copy.json")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceApplyCopy_UnsafePath_Blocks()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-apply-copy-unsafe-path");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            await WriteApplyCopyRequiredEvidenceAsync("run-1", workspacePath, sourceRepo, [("add", "../outside.txt"), ("add", ".git/config"), ("add", ".irondev/runs/x")]);

            using var doc = await RunWorkspaceApplyCopyAsync("run-1", workspacePath, expectedExitCode: 1);

            Assert.AreEqual("blocked", doc.RootElement.GetProperty("status").GetString());
            AssertArrayNotEmpty(doc.RootElement.GetProperty("errors"));
            Assert.IsFalse(File.Exists(Path.Combine(workspacePath, ".irondev", "runs", "run-1", "apply-copy.json")));
            Assert.IsFalse(File.Exists(Path.Combine(sourceRepo, "outside.txt")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceApplyCopy_SourceDirectoryConflict_Blocks()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-apply-copy-source-directory");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            Directory.CreateDirectory(Path.Combine(sourceRepo, "conflict"));
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            if (Directory.Exists(Path.Combine(workspacePath, "conflict")))
                Directory.Delete(Path.Combine(workspacePath, "conflict"), recursive: true);
            await File.WriteAllTextAsync(Path.Combine(workspacePath, "conflict"), "workspace file");
            await WriteApplyCopyRequiredEvidenceAsync("run-1", workspacePath, sourceRepo, [("add", "conflict")]);

            using var doc = await RunWorkspaceApplyCopyAsync("run-1", workspacePath, expectedExitCode: 1);

            Assert.AreEqual("blocked", doc.RootElement.GetProperty("status").GetString());
            AssertStringArrayContains(doc.RootElement.GetProperty("errors"), "source directory");
            Assert.IsTrue(Directory.Exists(Path.Combine(sourceRepo, "conflict")));
            Assert.IsFalse(File.Exists(Path.Combine(workspacePath, ".irondev", "runs", "run-1", "apply-copy.json")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceApplyCopy_NormalizedDuplicateOperation_BlocksBeforeCopy()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-apply-copy-normalized-duplicate");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            Directory.CreateDirectory(Path.Combine(workspacePath, "src"));
            await File.WriteAllTextAsync(Path.Combine(workspacePath, "src", "foo.txt"), "workspace file");
            await WriteApplyCopyRequiredEvidenceAsync("run-1", workspacePath, sourceRepo, [("add", "src/foo.txt"), ("add", "src\\foo.txt")]);

            using var doc = await RunWorkspaceApplyCopyAsync("run-1", workspacePath, expectedExitCode: 1);

            Assert.AreEqual("blocked", doc.RootElement.GetProperty("status").GetString());
            AssertStringArrayContains(doc.RootElement.GetProperty("errors"), "duplicate/conflicting operations");
            Assert.IsFalse(File.Exists(Path.Combine(sourceRepo, "src", "foo.txt")));
            Assert.IsFalse(File.Exists(Path.Combine(workspacePath, ".irondev", "runs", "run-1", "apply-copy.json")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public void DisposableWorkspaceApplyCopyService_DoesNotExecuteProcessesDeletePatchAgentsOrGit()
    {
        var serviceSource = File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "IronDev.Infrastructure", "Services", "Workspaces", "DisposableWorkspaceApplyCopyService.cs")));

        Assert.IsFalse(serviceSource.Contains("ProcessStartInfo", StringComparison.Ordinal));
        Assert.IsFalse(serviceSource.Contains("Process.Start", StringComparison.Ordinal));
        Assert.IsFalse(serviceSource.Contains("\"git\"", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(serviceSource.Contains("cmd.exe", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(serviceSource.Contains("powershell", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(serviceSource.Contains("/bin/sh", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(serviceSource.Contains("File.Delete", StringComparison.Ordinal));
        Assert.IsFalse(serviceSource.Contains("ApplyPatch", StringComparison.Ordinal));
        Assert.IsFalse(serviceSource.Contains("IAgent", StringComparison.Ordinal));
        Assert.IsFalse(serviceSource.Contains("SupervisorAgent", StringComparison.Ordinal));
    }

    [TestMethod]
    public void DisposableWorkspaceApplyDryRunService_DoesNotExecuteProcessesPatchAgentsOrMutateSource()
    {
        var serviceSource = File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "IronDev.Infrastructure", "Services", "Workspaces", "DisposableWorkspaceApplyDryRunService.cs")));

        Assert.IsFalse(serviceSource.Contains("ProcessStartInfo", StringComparison.Ordinal));
        Assert.IsFalse(serviceSource.Contains("Process.Start", StringComparison.Ordinal));
        Assert.IsFalse(serviceSource.Contains("\"git\"", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(serviceSource.Contains("cmd.exe", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(serviceSource.Contains("powershell", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(serviceSource.Contains("/bin/sh", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(serviceSource.Contains("File.Copy", StringComparison.Ordinal));
        Assert.IsFalse(serviceSource.Contains("File.Delete", StringComparison.Ordinal));
        Assert.IsFalse(serviceSource.Contains("ApplyAsync", StringComparison.Ordinal));
        Assert.IsFalse(serviceSource.Contains("ApplyPatch", StringComparison.Ordinal));
        Assert.IsFalse(serviceSource.Contains("IAgent", StringComparison.Ordinal));
        Assert.IsFalse(serviceSource.Contains("SupervisorAgent", StringComparison.Ordinal));
    }

    [TestMethod]
    public void DisposableWorkspaceApplyPreflightService_DoesNotExecuteProcessesPatchOrAgents()
    {
        var serviceSource = File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "IronDev.Infrastructure", "Services", "Workspaces", "DisposableWorkspaceApplyPreflightService.cs")));

        Assert.IsFalse(serviceSource.Contains("ProcessStartInfo", StringComparison.Ordinal));
        Assert.IsFalse(serviceSource.Contains("Process.Start", StringComparison.Ordinal));
        Assert.IsFalse(serviceSource.Contains("\"git\"", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(serviceSource.Contains("cmd.exe", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(serviceSource.Contains("powershell", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(serviceSource.Contains("/bin/sh", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(serviceSource.Contains("ApplyPatch", StringComparison.Ordinal));
        Assert.IsFalse(serviceSource.Contains("File.Copy", StringComparison.Ordinal));
        Assert.IsFalse(serviceSource.Contains("File.Delete", StringComparison.Ordinal));
        Assert.IsFalse(serviceSource.Contains("IAgent", StringComparison.Ordinal));
        Assert.IsFalse(serviceSource.Contains("SupervisorAgent", StringComparison.Ordinal));
    }

    [TestMethod]
    public void DisposableWorkspacePromotionApprovalService_DoesNotExecuteProcessesPatchOrAgents()
    {
        var serviceSource = File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "IronDev.Infrastructure", "Services", "Workspaces", "DisposableWorkspacePromotionApprovalService.cs")));

        Assert.IsFalse(serviceSource.Contains("ProcessStartInfo", StringComparison.Ordinal));
        Assert.IsFalse(serviceSource.Contains("Process.Start", StringComparison.Ordinal));
        Assert.IsFalse(serviceSource.Contains("\"git\"", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(serviceSource.Contains("cmd.exe", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(serviceSource.Contains("powershell", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(serviceSource.Contains("/bin/sh", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(serviceSource.Contains("ApplyAsync", StringComparison.Ordinal));
        Assert.IsFalse(serviceSource.Contains("ApplyPatch", StringComparison.Ordinal));
        Assert.IsFalse(serviceSource.Contains("IAgent", StringComparison.Ordinal));
        Assert.IsFalse(serviceSource.Contains("SupervisorAgent", StringComparison.Ordinal));
    }

    private static async Task<JsonDocument> RunWorkspacePromotionApprovalAsync(
        string runId,
        string workspacePath,
        string decision,
        string approvedBy,
        string reason,
        int expectedExitCode)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var result = await IronDevCli.RunAsync(
            [
                "workspace", "promotion-approval",
                "--run-id", runId,
                "--workspace-path", workspacePath,
                "--decision", decision,
                "--approved-by", approvedBy,
                "--reason", reason,
                "--json"
            ],
            output,
            error,
            handler: null,
            CancellationToken.None);

        Assert.AreEqual(expectedExitCode, result, error.ToString());
        AssertJsonWasWritten(output);
        return JsonDocument.Parse(output.ToString());
    }

    private static async Task<JsonDocument> RunWorkspaceApplyPreflightAsync(
        string runId,
        string workspacePath,
        int expectedExitCode)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var result = await IronDevCli.RunAsync(
            [
                "workspace", "apply-preflight",
                "--run-id", runId,
                "--workspace-path", workspacePath,
                "--json"
            ],
            output,
            error,
            handler: null,
            CancellationToken.None);

        Assert.AreEqual(expectedExitCode, result, error.ToString());
        AssertJsonWasWritten(output);
        return JsonDocument.Parse(output.ToString());
    }

    private static async Task<JsonDocument> RunWorkspaceApplyDryRunAsync(
        string runId,
        string workspacePath,
        int expectedExitCode)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var result = await IronDevCli.RunAsync(
            [
                "workspace", "apply-dry-run",
                "--run-id", runId,
                "--workspace-path", workspacePath,
                "--json"
            ],
            output,
            error,
            handler: null,
            CancellationToken.None);

        Assert.AreEqual(expectedExitCode, result, error.ToString());
        AssertJsonWasWritten(output);
        return JsonDocument.Parse(output.ToString());
    }

    private static async Task<JsonDocument> RunWorkspaceApplyCopyAsync(
        string runId,
        string workspacePath,
        int expectedExitCode)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var result = await IronDevCli.RunAsync(
            [
                "workspace", "apply-copy",
                "--run-id", runId,
                "--workspace-path", workspacePath,
                "--json"
            ],
            output,
            error,
            handler: null,
            CancellationToken.None);

        Assert.AreEqual(expectedExitCode, result, error.ToString());
        AssertJsonWasWritten(output);
        return JsonDocument.Parse(output.ToString());
    }

    private static async Task WriteApplyCopyRequiredEvidenceAsync(
        string runId,
        string workspacePath,
        string sourceRepo,
        IReadOnlyList<(string Operation, string RelativePath)> operations,
        bool dryRunReadyForApply = true,
        bool dryRunCanApplyNow = false,
        bool dryRunRequiresSeparateApplyCommand = true,
        string dryRunRecommendation = "ready_for_separate_apply_command")
    {
        var addedFiles = operations
            .Where(operation => string.Equals(operation.Operation, "add", StringComparison.OrdinalIgnoreCase))
            .Select(operation => operation.RelativePath)
            .ToArray();
        var modifiedFiles = operations
            .Where(operation => string.Equals(operation.Operation, "modify", StringComparison.OrdinalIgnoreCase))
            .Select(operation => operation.RelativePath)
            .ToArray();
        var deletedFiles = operations
            .Where(operation => string.Equals(operation.Operation, "delete", StringComparison.OrdinalIgnoreCase))
            .Select(operation => operation.RelativePath)
            .ToArray();

        var packagePath = await WritePromotionPackageAsync(runId, workspacePath, sourceRepo);
        await WriteDiffEvidenceAsync(runId, workspacePath, sourceRepo, addedFiles: addedFiles, modifiedFiles: modifiedFiles, deletedFiles: deletedFiles);
        await WriteApprovalEvidenceAsync(runId, workspacePath, packagePath, decision: "approved", allowsApply: false, requiresSeparateApplyCommand: true);
        await WriteApplyPreflightEvidenceAsync(runId, workspacePath, sourceRepo);
        await WriteApplyDryRunEvidenceAsync(
            runId,
            workspacePath,
            sourceRepo,
            operations,
            readyForApply: dryRunReadyForApply,
            canApplyNow: dryRunCanApplyNow,
            requiresSeparateApplyCommand: dryRunRequiresSeparateApplyCommand,
            recommendation: dryRunRecommendation);
    }

    private static async Task<string> WriteApplyDryRunEvidenceAsync(
        string runId,
        string workspacePath,
        string sourceRepo,
        IReadOnlyList<(string Operation, string RelativePath)> operations,
        bool readyForApply = true,
        bool canApplyNow = false,
        bool requiresSeparateApplyCommand = true,
        string recommendation = "ready_for_separate_apply_command")
    {
        var operationEntries = new List<object>();
        foreach (var operation in operations)
        {
            var sourcePath = Path.GetFullPath(Path.Combine(sourceRepo, operation.RelativePath));
            var operationWorkspacePath = Path.GetFullPath(Path.Combine(workspacePath, operation.RelativePath));
            var sourceExists = File.Exists(sourcePath);
            var workspaceExists = File.Exists(operationWorkspacePath);
            operationEntries.Add(new
            {
                operation = operation.Operation,
                relativePath = operation.RelativePath,
                sourcePath,
                workspacePath = operationWorkspacePath,
                sourceExists,
                workspaceExists,
                sourceDirectoryExists = Directory.Exists(sourcePath),
                workspaceDirectoryExists = Directory.Exists(operationWorkspacePath),
                sourceSha256 = sourceExists ? await ComputeSha256Async(sourcePath) : null,
                workspaceSha256 = workspaceExists ? await ComputeSha256Async(operationWorkspacePath) : null
            });
        }

        var dryRunPath = Path.Combine(workspacePath, ".irondev", "runs", runId, "apply-dry-run.json");
        Directory.CreateDirectory(Path.GetDirectoryName(dryRunPath)!);
        await File.WriteAllTextAsync(
            dryRunPath,
            JsonSerializer.Serialize(
                new
                {
                    runId,
                    workspacePath = Path.GetFullPath(workspacePath),
                    sourceRepo = Path.GetFullPath(sourceRepo),
                    createdUtc = DateTimeOffset.UtcNow,
                    readyForApply,
                    canApplyNow,
                    requiresSeparateApplyCommand,
                    recommendation,
                    operations = operationEntries,
                    addCount = operations.Count(operation => string.Equals(operation.Operation, "add", StringComparison.OrdinalIgnoreCase)),
                    modifyCount = operations.Count(operation => string.Equals(operation.Operation, "modify", StringComparison.OrdinalIgnoreCase)),
                    deleteCount = operations.Count(operation => string.Equals(operation.Operation, "delete", StringComparison.OrdinalIgnoreCase)),
                    evidencePaths = new[] { dryRunPath }
                },
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    WriteIndented = true
                }));
        return dryRunPath;
    }

    private static async Task<string> WriteApplyPreflightEvidenceAsync(
        string runId,
        string workspacePath,
        string sourceRepo,
        bool readyForApply = true,
        bool canApplyNow = false,
        bool requiresSeparateApplyCommand = true,
        string recommendation = "ready_for_separate_apply_command")
    {
        var preflightPath = Path.Combine(workspacePath, ".irondev", "runs", runId, "apply-preflight.json");
        Directory.CreateDirectory(Path.GetDirectoryName(preflightPath)!);
        await File.WriteAllTextAsync(
            preflightPath,
            JsonSerializer.Serialize(
                new
                {
                    runId,
                    workspacePath = Path.GetFullPath(workspacePath),
                    sourceRepo = Path.GetFullPath(sourceRepo),
                    approvalDecision = "approved",
                    approvedBy = "Rob",
                    approvalReason = "Reviewed validation and diff package.",
                    promotionPackagePath = Path.Combine(workspacePath, ".irondev", "runs", runId, "promotion-package.json"),
                    promotionPackageSha256 = "0".PadLeft(64, '0'),
                    approvalPromotionPackageSha256 = "0".PadLeft(64, '0'),
                    promotionPackageHashMatchesApproval = true,
                    recommendation,
                    validationSucceeded = true,
                    diffChanged = true,
                    readyForApply,
                    canApplyNow,
                    requiresSeparateApplyCommand,
                    applyPreflightPath = preflightPath,
                    blockers = Array.Empty<string>(),
                    errors = Array.Empty<string>(),
                    evidencePaths = new[] { preflightPath }
                },
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    WriteIndented = true
                }));
        return preflightPath;
    }

    private static async Task<string> WriteDiffEvidenceAsync(
        string runId,
        string workspacePath,
        string sourceRepo,
        bool changed = true,
        IReadOnlyList<string>? addedFiles = null,
        IReadOnlyList<string>? modifiedFiles = null,
        IReadOnlyList<string>? deletedFiles = null,
        bool includeSourceRepo = true)
    {
        modifiedFiles ??= ["README.md"];
        addedFiles ??= [];
        deletedFiles ??= [];
        var diffPath = Path.Combine(workspacePath, ".irondev", "runs", runId, "diff.json");
        Directory.CreateDirectory(Path.GetDirectoryName(diffPath)!);
        await File.WriteAllTextAsync(
            diffPath,
            JsonSerializer.Serialize(
                new
                {
                    runId,
                    workspacePath = Path.GetFullPath(workspacePath),
                    sourceRepo = includeSourceRepo ? Path.GetFullPath(sourceRepo) : null,
                    changed,
                    addedFiles,
                    modifiedFiles,
                    deletedFiles,
                    unchangedFileCount = 0,
                    diffMetadataPath = diffPath,
                    evidencePaths = new[] { diffPath }
                },
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    WriteIndented = true
                }));
        return diffPath;
    }

    private static async Task<string> WriteApprovalEvidenceAsync(
        string runId,
        string workspacePath,
        string promotionPackagePath,
        string decision,
        bool allowsApply,
        bool requiresSeparateApplyCommand,
        bool includeApprovedBy = true,
        bool includeReason = true,
        bool includePromotionPackagePath = true)
    {
        var approvalPath = Path.Combine(workspacePath, ".irondev", "runs", runId, "promotion-approval.json");
        Directory.CreateDirectory(Path.GetDirectoryName(approvalPath)!);
        await File.WriteAllTextAsync(
            approvalPath,
            JsonSerializer.Serialize(
                new
                {
                    runId,
                    workspacePath = Path.GetFullPath(workspacePath),
                    decision,
                    approvedBy = includeApprovedBy ? "Rob" : null,
                    reason = includeReason ? "Reviewed validation and diff package." : null,
                    createdUtc = DateTimeOffset.UtcNow,
                    promotionPackagePath = includePromotionPackagePath ? Path.GetFullPath(promotionPackagePath) : null,
                    promotionPackageSha256 = await ComputeSha256Async(promotionPackagePath),
                    approvalEvidencePath = approvalPath,
                    allowsApply,
                    requiresSeparateApplyCommand,
                    evidencePaths = new[] { promotionPackagePath, approvalPath }
                },
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    WriteIndented = true
                }));
        return approvalPath;
    }

    private static async Task<string> ComputeSha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        var hash = await System.Security.Cryptography.SHA256.HashDataAsync(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static async Task<string> WritePromotionPackageAsync(
        string runId,
        string workspacePath,
        string sourceRepo,
        bool requiresHumanApproval = true,
        bool canApplyToSourceRepo = false,
        bool autoPromotionAllowed = false,
        bool validationSucceeded = true,
        string recommendation = "ready_for_human_review",
        bool includeSourceRepo = true)
    {
        var packagePath = Path.Combine(workspacePath, ".irondev", "runs", runId, "promotion-package.json");
        Directory.CreateDirectory(Path.GetDirectoryName(packagePath)!);
        await File.WriteAllTextAsync(
            packagePath,
            JsonSerializer.Serialize(
                new
                {
                    runId,
                    workspacePath = Path.GetFullPath(workspacePath),
                    sourceRepo = includeSourceRepo ? Path.GetFullPath(sourceRepo) : null,
                    createdUtc = DateTimeOffset.UtcNow,
                    validation = new
                    {
                        status = validationSucceeded ? "succeeded" : "failed",
                        succeeded = validationSucceeded,
                        metadataPath = Path.Combine(workspacePath, ".irondev", "runs", runId, "validation.json")
                    },
                    diff = new
                    {
                        changed = true,
                        addedFiles = Array.Empty<string>(),
                        modifiedFiles = new[] { "README.md" },
                        deletedFiles = Array.Empty<string>(),
                        metadataPath = Path.Combine(workspacePath, ".irondev", "runs", runId, "diff.json")
                    },
                    approval = new
                    {
                        requiresHumanApproval,
                        canApplyToSourceRepo,
                        autoPromotionAllowed
                    },
                    recommendation,
                    riskNotes = new[] { "Changed files require human review before promotion." },
                    evidencePaths = new[] { Path.Combine(workspacePath, ".irondev", "runs", runId, "promotion-package.json") }
                },
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    WriteIndented = true
                }));
        return packagePath;
    }

    private static bool IsSha256Hex(string value)
    {
        return value.Length == 64 && value.All(character =>
            (character >= '0' && character <= '9') ||
            (character >= 'a' && character <= 'f') ||
            (character >= 'A' && character <= 'F'));
    }
}
