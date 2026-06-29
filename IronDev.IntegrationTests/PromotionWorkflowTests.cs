using IronDev.Core.Builder;
using IronDev.Core.Promotion;
using IronDev.Core.RunReports;
using IronDev.Core.Tools;
using IronDev.Infrastructure.Services.Promotion;
using IronDev.Infrastructure.Services.RunReports;
using IronDev.Infrastructure.Tools;
using IronDev.Infrastructure.Tools.CodeStandards;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using GovernedToolRegistry = IronDev.Infrastructure.Tools.GovernedToolRegistry;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class PromotionWorkflowTests
{
    private const string ApprovalPhrase = "Approve this specific promotion package for controlled worktree apply only.";

    [TestMethod]
    public async Task PatchProposalService_RunsCodeStandardsAndHashesExactPatch()
    {
        var service = CreatePatchProposalService();
        var proposal = await service.CreateAsync(new PatchProposalRequest
        {
            RunId = "run-patch-proposal",
            TicketId = 42,
            Proposal = Proposal(Patch()),
            PatchValidation = new PatchValidationResult { AllValid = true, Summary = "Patch validates." },
            BuildResult = Evidence("dotnet build", 0),
            TestResult = Evidence("dotnet test", 0)
        });

        Assert.AreEqual("run-patch-proposal", proposal.RunId);
        Assert.AreEqual(64, proposal.PatchSha256.Length);
        Assert.AreEqual("Passed", proposal.CodeStandardsStatus);
        Assert.AreEqual(0, proposal.CodeStandardsBlockingFindingCount);
        CollectionAssert.Contains(proposal.ChangedFiles.ToList(), "app.txt");
    }

    [TestMethod]
    public async Task PromotionApproval_ReferencesExactPackagePatchHash()
    {
        var package = await CreatePackageAsync();
        var approval = new ControlledWriteApprovalService().ApproveForControlledWorktreeApply(new ControlledWriteApprovalRequest
        {
            RunId = "approval-run",
            TraceId = "trace-approval",
            Project = "Synthetic",
            Package = package,
            ApprovedBy = "bob",
            ApprovalRole = "HumanOwner",
            ApprovalPhrase = ApprovalPhrase
        });

        Assert.AreEqual("Approved", approval.ApprovalState);
        Assert.AreEqual(package.PackageId, approval.PackageId);
        Assert.AreEqual(package.PatchSha256, approval.PatchSha256);
        Assert.IsTrue(approval.AllowedActions.Contains("ControlledWorktreeApply"));
        Assert.IsFalse(approval.ValidForRealRepoWrite);
    }

    [TestMethod]
    public async Task ControlledWorktreeApply_BlocksMismatchedPatchHash()
    {
        var package = await CreatePackageAsync();
        var approval = new ControlledWriteApprovalService().ApproveForControlledWorktreeApply(new ControlledWriteApprovalRequest
        {
            RunId = "approval-run",
            TraceId = "trace-approval",
            Project = "Synthetic",
            Package = package,
            ApprovedBy = "bob",
            ApprovalRole = "HumanOwner",
            ApprovalPhrase = ApprovalPhrase
        }) with
        {
            PatchSha256 = "not-the-package-hash"
        };
        var (repo, worktreeRoot) = await CreateGitRepoAsync();
        try
        {
            var report = await new ControlledWorktreeApplyService(new InMemoryRunEventStore()).ApplyAsync(new ControlledWorktreeApplyRequest
            {
                RunId = "apply-mismatch",
                TraceId = "trace-apply",
                ActiveRepoPath = repo,
                TargetWorktreePath = Path.Combine(worktreeRoot, "candidate"),
                TargetBranchName = "ida/test/mismatch",
                Package = package,
                Approval = approval
            });

            Assert.AreEqual("Blocked", report.Status);
            Assert.IsTrue(report.Errors.Any(error => error.Contains("patch hash", StringComparison.OrdinalIgnoreCase)));
            Assert.IsFalse(Directory.Exists(report.TargetWorktreePath));
        }
        finally
        {
            DeleteIfExists(Path.GetDirectoryName(repo)!);
        }
    }

    [TestMethod]
    public async Task ControlledWorktreeApply_BlocksDirtyActiveTree()
    {
        var package = await CreatePackageAsync();
        var approval = Approved(package);
        var (repo, worktreeRoot) = await CreateGitRepoAsync();
        await File.WriteAllTextAsync(Path.Combine(repo, "dirty.txt"), "dirty");

        try
        {
            var report = await new ControlledWorktreeApplyService(new InMemoryRunEventStore()).ApplyAsync(new ControlledWorktreeApplyRequest
            {
                RunId = "apply-dirty",
                TraceId = "trace-apply",
                ActiveRepoPath = repo,
                TargetWorktreePath = Path.Combine(worktreeRoot, "candidate"),
                TargetBranchName = "ida/test/dirty",
                Package = package,
                Approval = approval
            });

            Assert.AreEqual("Blocked", report.Status);
            Assert.IsTrue(report.Errors.Any(error => error.Contains("uncommitted changes", StringComparison.OrdinalIgnoreCase)));
            Assert.AreEqual("dirty", await File.ReadAllTextAsync(Path.Combine(repo, "dirty.txt")));
        }
        finally
        {
            DeleteIfExists(Path.GetDirectoryName(repo)!);
        }
    }

    [TestMethod]
    public async Task ControlledWorktreeApply_AppliesApprovedPatchOnlyInsideTargetWorktree()
    {
        var package = await CreatePackageAsync();
        var approval = Approved(package);
        var events = new InMemoryRunEventStore();
        var (repo, worktreeRoot) = await CreateGitRepoAsync();
        var target = Path.Combine(worktreeRoot, "candidate");

        try
        {
            var report = await new ControlledWorktreeApplyService(events).ApplyAsync(new ControlledWorktreeApplyRequest
            {
                RunId = "apply-success",
                TraceId = "trace-apply",
                ActiveRepoPath = repo,
                TargetWorktreePath = target,
                TargetBranchName = "ida/test/apply-success",
                Package = package,
                Approval = approval
            });

            Assert.AreEqual("Applied", report.Status, string.Join(" | ", report.Errors));
            Assert.AreEqual(0, report.Mutation.ActiveRepoMutationCount);
            Assert.AreEqual("before\n", Normalize(await File.ReadAllTextAsync(Path.Combine(repo, "app.txt"))));
            Assert.AreEqual("after\n", Normalize(await File.ReadAllTextAsync(Path.Combine(target, "app.txt"))));
            Assert.IsTrue((await events.GetEventsAsync("apply-success")).Any(runEvent =>
                runEvent.EventType == "ControlledWorktreeApplyCompleted"));
        }
        finally
        {
            await TryGitAsync(repo, ["worktree", "remove", "--force", target]);
            DeleteIfExists(Path.GetDirectoryName(repo)!);
        }
    }

    private static async Task<PromotionPackage> CreatePackageAsync()
    {
        var patchProposal = await CreatePatchProposalService().CreateAsync(new PatchProposalRequest
        {
            RunId = "source-run",
            TicketId = 42,
            Proposal = Proposal(Patch()),
            PatchValidation = new PatchValidationResult { AllValid = true, Summary = "Patch validates." },
            BuildResult = Evidence("dotnet build", 0),
            TestResult = Evidence("dotnet test", 0)
        });

        return new PromotionPackageService().CreatePackage(new PromotionPackageRequest
        {
            Project = "Synthetic",
            SourceTraceId = "trace-source",
            PatchProposal = patchProposal,
            RuntimeProfile = new LanguageRuntimeRegistry().GetRequired("csharp-dotnet")
        });
    }

    private static ControlledWriteApprovalRecord Approved(PromotionPackage package) =>
        new ControlledWriteApprovalService().ApproveForControlledWorktreeApply(new ControlledWriteApprovalRequest
        {
            RunId = "approval-run",
            TraceId = "trace-approval",
            Project = "Synthetic",
            Package = package,
            ApprovedBy = "bob",
            ApprovalRole = "HumanOwner",
            ApprovalPhrase = ApprovalPhrase
        });

    private static IPatchProposalService CreatePatchProposalService()
    {
        var tool = new CodeStandardsAnalysisTool();
        return new PatchProposalService(new GovernedToolRegistry([tool], new GovernedToolPolicyEvaluator()));
    }

    private static CodeChangeProposal Proposal(string patch) => new()
    {
        TicketId = 42,
        Summary = "Update app text.",
        Rationale = "Synthetic test patch.",
        RiskNotes = "Low.",
        TestPlan = "Run synthetic test.",
        FileChanges =
        [
            new FileChangeProposal
            {
                FilePath = "app.txt",
                Patch = patch
            }
        ]
    };

    private static string Patch() =>
        string.Join('\n',
        [
            "diff --git a/app.txt b/app.txt",
            "--- a/app.txt",
            "+++ b/app.txt",
            "@@ -1,1 +1,1 @@",
            "-before",
            "+after",
            string.Empty
        ]);

    private static RuntimeCommandEvidence Evidence(string command, int exitCode) => new()
    {
        Command = command,
        ExitCode = exitCode,
        Status = exitCode == 0 ? "Succeeded" : "Failed",
        LogPath = $"{command}.log",
        Summary = $"{command} exit={exitCode}."
    };

    private static async Task<(string Repo, string WorktreeRoot)> CreateGitRepoAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"irondev-promotion-{Guid.NewGuid():N}");
        var repo = Path.Combine(root, "repo");
        var worktrees = Path.Combine(root, "worktrees");
        Directory.CreateDirectory(repo);
        Directory.CreateDirectory(worktrees);
        await GitAsync(repo, ["init"]);
        await GitAsync(repo, ["config", "user.email", "irondev@example.test"]);
        await GitAsync(repo, ["config", "user.name", "IronDev Test"]);
        await File.WriteAllTextAsync(Path.Combine(repo, "app.txt"), "before\n");
        await GitAsync(repo, ["add", "app.txt"]);
        await GitAsync(repo, ["commit", "-m", "initial"]);
        return (repo, worktrees);
    }

    private static async Task TryGitAsync(string workingDirectory, IReadOnlyList<string> args)
    {
        try
        {
            await GitAsync(workingDirectory, args);
        }
        catch
        {
        }
    }

    private static async Task GitAsync(string workingDirectory, IReadOnlyList<string> args)
    {
        var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo("git")
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };

        foreach (var arg in args)
            process.StartInfo.ArgumentList.Add(arg);

        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {stdout} {stderr}");
    }

    private static string Normalize(string value) =>
        value.Replace("\r\n", "\n");

    private static void DeleteIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);

            foreach (var directory in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories))
                File.SetAttributes(directory, FileAttributes.Normal);

            File.SetAttributes(path, FileAttributes.Normal);
            Directory.Delete(path, recursive: true);
        }
    }
}
