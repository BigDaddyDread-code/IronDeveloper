using System.Diagnostics;
using IronDev.Core.Validation;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockBK0ValidationRuntimeTests
{
    [TestMethod]
    public async Task BlockBK0_SupervisedRunner_CapturesStdoutStderrAndExitCode()
    {
        var root = CreateTempRoot();
        try
        {
            var result = await new SupervisedProcessRunner().RunAsync(new ValidationCommandSpec
            {
                LaneName = "bk0-capture",
                Command = "powershell",
                Arguments = ["-NoProfile", "-Command", "Write-Output 'hello-bk0'; [Console]::Error.WriteLine('err-bk0'); exit 7"],
                WorkingDirectory = root,
                Timeout = TimeSpan.FromSeconds(10),
                StdoutPath = Path.Combine(root, "stdout.log"),
                StderrPath = Path.Combine(root, "stderr.log"),
                CommandKind = ValidationCommandKind.Generic
            }).ConfigureAwait(false);

            Assert.AreEqual("bk0-capture", result.LaneName);
            Assert.AreEqual(7, result.ExitCode);
            Assert.AreEqual(ValidationFailureKind.ProcessExitNonZero, result.FailureClassification);
            StringAssert.Contains(File.ReadAllText(result.StdoutPath), "hello-bk0");
            StringAssert.Contains(File.ReadAllText(result.StderrPath), "err-bk0");
            StringAssert.Contains(result.StdoutTail, "hello-bk0");
            StringAssert.Contains(result.StderrTail, "err-bk0");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [TestMethod]
    public async Task BlockBK0_SupervisedRunner_TimesOutAndKillsProcessTree()
    {
        var root = CreateTempRoot();
        var childPidPath = Path.Combine(root, "child.pid");
        int? childPid = null;
        try
        {
            var result = await new SupervisedProcessRunner().RunAsync(new ValidationCommandSpec
            {
                LaneName = "bk0-timeout",
                Command = "powershell",
                Arguments =
                [
                    "-NoProfile",
                    "-Command",
                    $"$p = Start-Process powershell -PassThru -ArgumentList '-NoProfile','-Command','Start-Sleep -Seconds 60'; $p.Id | Out-File -Encoding ascii -FilePath '{childPidPath}'; Start-Sleep -Seconds 60"
                ],
                WorkingDirectory = root,
                Timeout = TimeSpan.FromSeconds(2),
                StdoutPath = Path.Combine(root, "timeout.stdout.log"),
                StderrPath = Path.Combine(root, "timeout.stderr.log"),
                CommandKind = ValidationCommandKind.Test
            }).ConfigureAwait(false);

            Assert.IsTrue(File.Exists(childPidPath), "child pid file should be written before timeout");
            childPid = int.Parse(File.ReadAllText(childPidPath).Trim());
            Assert.IsTrue(result.TimedOut);
            Assert.IsTrue(result.ProcessTreeKillAttempted);
            Assert.IsTrue(result.ProcessTreeKillSucceeded);
            Assert.AreEqual(ValidationFailureKind.Timeout, result.FailureClassification);
            Assert.IsTrue(File.Exists(result.StdoutPath));
            Assert.IsTrue(File.Exists(result.StderrPath));
            Assert.IsTrue(await WaitUntilProcessExitsAsync(childPid.Value).ConfigureAwait(false), $"child process {childPid.Value} was still running after process-tree kill");
        }
        finally
        {
            if (childPid is not null)
                TryKill(childPid.Value);
            TryDelete(root);
        }
    }

    [TestMethod]
    public void BlockBK0_FailureClassifier_SeparatesRestoreAccessFromProductFailure()
    {
        var accessDenied = ValidationFailureClassifier.Classify(
            ValidationCommandKind.Restore,
            exitCode: 1,
            timedOut: false,
            cancelled: false,
            stdout: string.Empty,
            stderr: "Failed to read NuGet.Config because unauthorized access to path was denied.");

        var buildFailure = ValidationFailureClassifier.Classify(
            ValidationCommandKind.Build,
            exitCode: 1,
            timedOut: false,
            cancelled: false,
            stdout: string.Empty,
            stderr: "CS1002 ; expected");

        Assert.AreEqual(ValidationFailureKind.EnvironmentAccessDenied, accessDenied);
        Assert.AreEqual(ValidationFailureKind.BuildFailed, buildFailure);
    }

    [TestMethod]
    public void BlockBK0_GeneratedArtifactAndBoundaryPolicy_StayEvidenceOnly()
    {
        var assets = ValidationGeneratedArtifactInspector.Classify("IronDev.Core/obj/project.assets.json");
        var tempConfig = ValidationGeneratedArtifactInspector.Classify("NuGet.Config");
        var source = ValidationGeneratedArtifactInspector.Classify("IronDev.Core/Validation/ValidationLanePlanner.cs");
        var boundary = ValidationRuntimeBoundary.Evidence;

        Assert.AreEqual(ValidationChangedFileKind.GeneratedRestoreArtifact, assets.Kind);
        Assert.AreEqual(ValidationChangedFileKind.TemporaryNuGetConfig, tempConfig.Kind);
        Assert.AreEqual(ValidationChangedFileKind.Source, source.Kind);
        Assert.IsTrue(boundary.EvidenceOnly);
        Assert.IsFalse(boundary.CanApprove);
        Assert.IsFalse(boundary.CanMerge);
        Assert.IsFalse(boundary.CanRelease);
        Assert.IsFalse(boundary.CanDeploy);
        Assert.IsFalse(boundary.CanMutateSource);
        Assert.IsFalse(boundary.CanPromoteMemory);
        Assert.IsFalse(boundary.CanSatisfyPolicy);
        Assert.IsFalse(boundary.CanContinueWorkflow);
    }

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "irondev-bk0-runtime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
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
            // Best-effort cleanup for Windows process/file timing.
        }
    }

    private static async Task<bool> WaitUntilProcessExitsAsync(int pid)
    {
        for (var i = 0; i < 20; i++)
        {
            if (!IsProcessRunning(pid))
                return true;
            await Task.Delay(250).ConfigureAwait(false);
        }

        return !IsProcessRunning(pid);
    }

    private static bool IsProcessRunning(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static void TryKill(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best-effort cleanup for failed process-tree canaries.
        }
    }
}
