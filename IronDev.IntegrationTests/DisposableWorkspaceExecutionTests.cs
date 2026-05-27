using IronDev.Core.Runs;
using IronDev.Core.RunReports;
using IronDev.Core.Workspaces;
using IronDev.Infrastructure.Services.Runs;
using IronDev.Infrastructure.Services.RunReports;
using IronDev.Infrastructure.Services.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class DisposableWorkspaceExecutionTests
{
    [TestMethod]
    public async Task RunAsync_ExecutesCommandInDisposableWorkspaceWithoutMutatingSource()
    {
        var (source, workspaceRoot) = CreateWorkspaceFixture();
        try
        {
            var runs = new InMemoryRunStore();
            var events = new InMemoryRunEventStore();
            var run = await runs.CreateAsync(new CreateRunRequest
            {
                RunId = "disposable-success",
                IsDisposable = true,
                Summary = "Synthetic disposable command test."
            });
            var service = new DisposableWorkspaceExecutionService(runs, events);

            var result = await service.RunAsync(new DisposableWorkspaceRunRequest
            {
                RunId = run.RunId,
                SourcePath = source,
                WorkspaceRoot = workspaceRoot,
                Commands =
                [
                    new DisposableWorkspaceCommand
                    {
                        FileName = "cmd.exe",
                        Arguments = ["/c", "echo workspace-only> generated.txt"],
                        DisplayName = "write synthetic output",
                        Timeout = TimeSpan.FromSeconds(20)
                    }
                ]
            });

            Assert.IsTrue(result.Succeeded);
            Assert.IsTrue(result.CleanedUp);
            Assert.IsFalse(File.Exists(Path.Combine(source, "generated.txt")));
            Assert.IsFalse(Directory.Exists(result.WorkspacePath));

            var finalRun = await runs.GetAsync(run.RunId);
            Assert.IsNotNull(finalRun);
            Assert.AreEqual(RunLifecycleState.Completed, finalRun.State);

            var runEvents = await events.GetEventsAsync(run.RunId);
            Assert.IsTrue(runEvents.Any(e => e.EventType == "DisposableWorkspaceCreated"));
            Assert.IsTrue(runEvents.Any(e => e.EventType == "DisposableCommandCompleted"));
            Assert.IsTrue(runEvents.Any(e => e.EventType == "DisposableWorkspaceCleaned"));
            Assert.IsTrue(runEvents.Any(e => e.EventType == "RunCompleted"));
        }
        finally
        {
            DeleteIfExists(source);
            DeleteIfExists(workspaceRoot);
        }
    }

    [TestMethod]
    public async Task RunAsync_CleansDisposableWorkspaceAfterCommandFailure()
    {
        var (source, workspaceRoot) = CreateWorkspaceFixture();
        try
        {
            var runs = new InMemoryRunStore();
            var events = new InMemoryRunEventStore();
            var run = await runs.CreateAsync(new CreateRunRequest
            {
                RunId = "disposable-failure",
                IsDisposable = true,
                Summary = "Synthetic disposable failure test."
            });
            var service = new DisposableWorkspaceExecutionService(runs, events);

            var result = await service.RunAsync(new DisposableWorkspaceRunRequest
            {
                RunId = run.RunId,
                SourcePath = source,
                WorkspaceRoot = workspaceRoot,
                Commands =
                [
                    new DisposableWorkspaceCommand
                    {
                        FileName = "cmd.exe",
                        Arguments = ["/c", "echo failing-workspace> generated.txt && exit /b 7"],
                        DisplayName = "fail synthetic output",
                        Timeout = TimeSpan.FromSeconds(20)
                    }
                ]
            });

            Assert.IsFalse(result.Succeeded);
            Assert.IsTrue(result.CleanedUp);
            Assert.IsFalse(File.Exists(Path.Combine(source, "generated.txt")));
            Assert.IsFalse(Directory.Exists(result.WorkspacePath));

            var finalRun = await runs.GetAsync(run.RunId);
            Assert.IsNotNull(finalRun);
            Assert.AreEqual(RunLifecycleState.Failed, finalRun.State);
            StringAssert.Contains(finalRun.FailureReason!, "exit code 7");

            var runEvents = await events.GetEventsAsync(run.RunId);
            Assert.IsTrue(runEvents.Any(e => e.EventType == "DisposableCommandFailed"));
            Assert.IsTrue(runEvents.Any(e => e.EventType == "DisposableWorkspaceCleaned"));
            Assert.IsTrue(runEvents.Any(e => e.EventType == "RunFailed"));
        }
        finally
        {
            DeleteIfExists(source);
            DeleteIfExists(workspaceRoot);
        }
    }

    private static (string Source, string WorkspaceRoot) CreateWorkspaceFixture()
    {
        var root = Path.Combine(Path.GetTempPath(), $"irondev-disposable-test-{Guid.NewGuid():N}");
        var source = Path.Combine(root, "source");
        var workspaceRoot = Path.Combine(root, "workspaces");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(workspaceRoot);
        File.WriteAllText(Path.Combine(source, "marker.txt"), "source");
        return (source, workspaceRoot);
    }

    private static void DeleteIfExists(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}
