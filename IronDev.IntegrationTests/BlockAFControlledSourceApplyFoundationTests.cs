using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Cli;
using IronDev.Core.Governance;
using IronDev.Core.SourceApply;
using SourceApplyDryRunResult = IronDev.Core.SourceApply.SourceApplyDryRunResult;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockAFControlledSourceApplyFoundationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [TestMethod]
    public void BlockAF_ActionSpine_RegistersPreparationActionsWithoutMakingSourceApplyExecutable()
    {
        foreach (var action in new[]
                 {
                     GovernedActionKind.SourceApplyRequestCreated,
                     GovernedActionKind.PatchArtifactVerified,
                     GovernedActionKind.SourceApplyApprovalEvidenceRead,
                     GovernedActionKind.SourceApplyGateEvaluated,
                     GovernedActionKind.SourceApplyDryRunStarted,
                     GovernedActionKind.SourceApplyDryRunCompleted,
                     GovernedActionKind.RollbackPlanDrafted,
                     GovernedActionKind.SourceApplyReadinessReportCreated
                 })
        {
            var entry = AuthorityActionInventory.Get(action);
            Assert.AreEqual(GovernedActionClassification.NonAuthority, entry.Classification, action.ToString());
            Assert.IsTrue(entry.AllowedInCurrentBlock, action.ToString());
            Assert.IsFalse(entry.RequiresConscience, action.ToString());
            Assert.IsFalse(entry.RequiresThoughtLedger, action.ToString());
        }

        var sourceApply = AuthorityActionInventory.Get(GovernedActionKind.SourceApply);
        Assert.AreEqual(GovernedActionClassification.AuthorityBearing, sourceApply.Classification);
        Assert.IsFalse(sourceApply.AllowedInCurrentBlock);
        Assert.IsFalse(ConscienceDecisionEvaluator.Evaluate(GovernedActionKind.SourceApply, AllowDecision()).IsExecutable);
    }

    [TestMethod]
    public void BlockAF_ReadinessEnum_CannotRepresentApplyApprovalReleaseMergeOrDeploy()
    {
        var names = Enum.GetNames<SourceApplyReadiness>();
        foreach (var forbidden in new[] { "Applied", "Approved", "ReleaseReady", "MergeReady", "DeployReady", "PolicySatisfied" })
            Assert.IsFalse(names.Any(name => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase)), forbidden);

        CollectionAssert.AreEquivalent(new[] { "ReadyForFutureControlledApply", "Blocked" }, names);
    }

    [TestMethod]
    public async Task BlockAF_SourceApplyPrepare_WithValidApprovalCreatesReadinessPackageWithoutMutatingSource()
    {
        using var fixture = await PatchRunFixture.CreateFinishedRunAsync("af-ready").ConfigureAwait(false);
        var approvalPath = Path.Combine(fixture.RootPath, "approval.json");
        Assert.AreEqual(0, (await RunCliAsync("source-apply", "approval-template", "--run", fixture.RunPath, "--out", approvalPath, "--json").ConfigureAwait(false)).ExitCode);
        var template = JsonSerializer.Deserialize<SourceApplyApprovalEvidence>(await File.ReadAllTextAsync(approvalPath).ConfigureAwait(false), JsonOptions)!;
        var approval = template with
        {
            ApprovedBy = "human-reviewer",
            ApprovalText = "Human reviewed the patch evidence and approves dry-run readiness evaluation only.",
            ApprovedAtUtc = DateTimeOffset.UtcNow
        };
        await File.WriteAllTextAsync(approvalPath, JsonSerializer.Serialize(approval, JsonOptions)).ConfigureAwait(false);

        var sourceStatusBefore = RunProcess("git", ["status", "--porcelain=v1"], fixture.SourceRepoPath).Stdout;
        var prepare = await RunCliAsync("source-apply", "prepare", "--run", fixture.RunPath, "--approval", approvalPath, "--apply-root", Path.Combine(fixture.RootPath, "apply-root"), "--json").ConfigureAwait(false);
        var sourceStatusAfter = RunProcess("git", ["status", "--porcelain=v1"], fixture.SourceRepoPath).Stdout;

        Assert.AreEqual(0, prepare.ExitCode, prepare.Error + prepare.Output);
        Assert.AreEqual(sourceStatusBefore, sourceStatusAfter, "source repository status changed");
        AssertArtifactExists(fixture.RunPath, "source-apply-request.json");
        AssertArtifactExists(fixture.RunPath, "source-apply-approval-template.json");
        AssertArtifactExists(fixture.RunPath, "source-apply-approval-evidence.json");
        AssertArtifactExists(fixture.RunPath, "patch-artifact-verification.json");
        AssertArtifactExists(fixture.RunPath, "source-apply-gate-decision.json");
        AssertArtifactExists(fixture.RunPath, "source-apply-dry-run-plan.json");
        AssertArtifactExists(fixture.RunPath, "source-apply-dry-run-result.json");
        AssertArtifactExists(fixture.RunPath, "rollback-plan-draft.md");
        AssertArtifactExists(fixture.RunPath, "rollback-plan-draft.json");
        AssertArtifactExists(fixture.RunPath, "reverse-patch.diff");
        AssertArtifactExists(fixture.RunPath, "source-apply-readiness-report.md");
        AssertArtifactExists(fixture.RunPath, "source-apply-readiness.json");
        AssertArtifactExists(Path.Combine(fixture.RunPath, "source-apply-output"), "dry-run.stdout.txt");
        AssertArtifactExists(Path.Combine(fixture.RunPath, "source-apply-output"), "dry-run.stderr.txt");
        AssertArtifactExists(Path.Combine(fixture.RunPath, "source-apply-output"), "dry-run.combined.txt");

        var gate = ReadJson<SourceApplyGateDecision>(Path.Combine(fixture.RunPath, "source-apply-gate-decision.json"));
        Assert.AreEqual(SourceApplyGateDecisionOutcome.AllowDryRun, gate.Decision);
        Assert.IsFalse(gate.Boundary.SourceApplied);
        Assert.IsFalse(gate.Boundary.ApprovalGranted);

        var dryRun = ReadJson<SourceApplyDryRunResult>(Path.Combine(fixture.RunPath, "source-apply-dry-run-result.json"));
        Assert.IsTrue(dryRun.PatchAppliedInRehearsalWorkspace);
        Assert.IsFalse(dryRun.SourceRepoMutated);
        Assert.IsTrue(Directory.Exists(dryRun.RehearsalWorkspacePath));
        Assert.IsFalse(IsSameOrUnderPath(dryRun.RehearsalWorkspacePath, fixture.SourceRepoPath));

        var readiness = ReadJson<SourceApplyReadinessReport>(Path.Combine(fixture.RunPath, "source-apply-readiness.json"));
        Assert.AreEqual(SourceApplyReadiness.ReadyForFutureControlledApply, readiness.Readiness);
        Assert.IsFalse(readiness.Boundary.SourceApplied);
        Assert.IsFalse(readiness.Boundary.ReleaseApproved);

        var reportText = File.ReadAllText(Path.Combine(fixture.RunPath, "source-apply-readiness-report.md"));
        StringAssert.Contains(reportText, "It does not apply source.");
        StringAssert.Contains(reportText, "A successful dry-run is not source apply.");
        Assert.IsFalse(reportText.Contains("merge ready", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(reportText.Contains("release ready", StringComparison.OrdinalIgnoreCase));

        var events = File.ReadAllText(Path.Combine(fixture.RunPath, "governance-events.jsonl"));
        foreach (var action in new[]
                 {
                     nameof(GovernedActionKind.SourceApplyRequestCreated),
                     nameof(GovernedActionKind.PatchArtifactVerified),
                     nameof(GovernedActionKind.SourceApplyApprovalEvidenceRead),
                     nameof(GovernedActionKind.SourceApplyGateEvaluated),
                     nameof(GovernedActionKind.SourceApplyDryRunStarted),
                     nameof(GovernedActionKind.SourceApplyDryRunCompleted),
                     nameof(GovernedActionKind.RollbackPlanDrafted),
                     nameof(GovernedActionKind.SourceApplyReadinessReportCreated)
                 })
        {
            StringAssert.Contains(events, action);
        }
    }

    [TestMethod]
    public async Task BlockAF_SourceApplyPrepare_BlocksWithoutApprovalAndDoesNotDryRun()
    {
        using var fixture = await PatchRunFixture.CreateFinishedRunAsync("af-no-approval").ConfigureAwait(false);

        var prepare = await RunCliAsync("source-apply", "prepare", "--run", fixture.RunPath, "--apply-root", Path.Combine(fixture.RootPath, "apply-root"), "--json").ConfigureAwait(false);

        Assert.AreEqual(1, prepare.ExitCode);
        var gate = ReadJson<SourceApplyGateDecision>(Path.Combine(fixture.RunPath, "source-apply-gate-decision.json"));
        Assert.AreEqual(SourceApplyGateDecisionOutcome.Block, gate.Decision);
        CollectionAssert.Contains(gate.Reasons, "MissingApprovalEvidence");
        Assert.IsFalse(File.Exists(Path.Combine(fixture.RunPath, "source-apply-dry-run-result.json")));
        Assert.AreEqual(string.Empty, RunProcess("git", ["status", "--porcelain=v1"], fixture.SourceRepoPath).Stdout.Trim());
    }

    [TestMethod]
    public async Task BlockAF_SourceApplyPrepare_BlocksMismatchedApprovalEvidence()
    {
        using var fixture = await PatchRunFixture.CreateFinishedRunAsync("af-bad-approval").ConfigureAwait(false);
        var approvalPath = Path.Combine(fixture.RootPath, "approval.json");
        Assert.AreEqual(0, (await RunCliAsync("source-apply", "approval-template", "--run", fixture.RunPath, "--out", approvalPath, "--json").ConfigureAwait(false)).ExitCode);
        var template = JsonSerializer.Deserialize<SourceApplyApprovalEvidence>(await File.ReadAllTextAsync(approvalPath).ConfigureAwait(false), JsonOptions)!;
        var approval = template with
        {
            ApprovedBy = "human-reviewer",
            ApprovalText = "Human reviewed the patch evidence and approves dry-run readiness evaluation only.",
            PatchSha256 = "mismatched-patch-hash",
            ApprovedAtUtc = DateTimeOffset.UtcNow
        };
        await File.WriteAllTextAsync(approvalPath, JsonSerializer.Serialize(approval, JsonOptions)).ConfigureAwait(false);

        var prepare = await RunCliAsync("source-apply", "prepare", "--run", fixture.RunPath, "--approval", approvalPath, "--json").ConfigureAwait(false);

        Assert.AreEqual(1, prepare.ExitCode);
        var gate = ReadJson<SourceApplyGateDecision>(Path.Combine(fixture.RunPath, "source-apply-gate-decision.json"));
        Assert.AreEqual(SourceApplyGateDecisionOutcome.Block, gate.Decision);
        CollectionAssert.Contains(gate.Reasons, "ApprovalPatchHashMismatch");
        Assert.IsFalse(File.Exists(Path.Combine(fixture.RunPath, "source-apply-dry-run-result.json")));
    }

    [TestMethod]
    public void BlockAF_PatchArtifactVerifier_BlocksGitDirectoryAndMissingManualApplyInstructions()
    {
        var root = CreateTempRoot();
        try
        {
            var runPath = Path.Combine(root, "run");
            Directory.CreateDirectory(runPath);
            File.WriteAllText(Path.Combine(runPath, "run.json"), "{}");
            File.WriteAllText(Path.Combine(runPath, "patch.diff"), "diff --git a/.git/config b/.git/config\n--- a/.git/config\n+++ b/.git/config\n@@ -0,0 +1 @@\n+bad\n");
            File.WriteAllText(Path.Combine(runPath, "changed-files.txt"), ".git/config\n");
            var metadata = new SourceApplyRunMetadata
            {
                RunId = "af-verify",
                RunPath = runPath,
                SourceRepoPath = root,
                SourceRepoIdentity = "fixture",
                BaseBranch = "main",
                BaseCommit = "abc",
                ChangedFiles = [".git/config"]
            };

            var verification = PatchArtifactVerifier.Verify(metadata);

            Assert.AreEqual(PatchArtifactVerificationDecision.Blocked, verification.Decision);
            CollectionAssert.Contains(verification.Reasons, "PatchTouchesGitDirectory");
            CollectionAssert.Contains(verification.Reasons, "MissingManualApplyInstructions");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [TestMethod]
    public async Task BlockAF_SourceApplyStatus_IsReadOnlyInspection()
    {
        using var fixture = await PatchRunFixture.CreateFinishedRunAsync("af-status").ConfigureAwait(false);
        Assert.AreEqual(1, (await RunCliAsync("source-apply", "prepare", "--run", fixture.RunPath, "--json").ConfigureAwait(false)).ExitCode);

        var statusBefore = RunProcess("git", ["status", "--porcelain=v1"], fixture.SourceRepoPath).Stdout;
        var status = await RunCliAsync("source-apply", "status", "--run", fixture.RunPath, "--json").ConfigureAwait(false);
        var statusAfter = RunProcess("git", ["status", "--porcelain=v1"], fixture.SourceRepoPath).Stdout;

        Assert.AreEqual(0, status.ExitCode, status.Error + status.Output);
        StringAssert.Contains(status.Output, "Blocked");
        Assert.AreEqual(statusBefore, statusAfter);
    }

    [TestMethod]
    public async Task BlockAF_ForbiddenSourceApplySubcommands_DoNotExist()
    {
        foreach (var subcommand in new[] { "apply", "commit", "push", "pr", "merge", "rollback" })
        {
            var result = await RunCliAsync("source-apply", subcommand, "--run", "any").ConfigureAwait(false);
            Assert.AreEqual(2, result.ExitCode, subcommand);
            StringAssert.Contains(result.Error, "intentionally unsupported");
        }
    }

    [TestMethod]
    public void BlockAF_StaticBoundary_DoesNotAddSqlApiUiRuntimeOrRealSourceApplyPath()
    {
        var repoRoot = FindRepositoryRoot();
        var sourceApplyFiles = Directory.GetFiles(Path.Combine(repoRoot, "IronDev.Core", "SourceApply"), "*.cs")
            .Concat([Path.Combine(repoRoot, "tools", "IronDev.Cli", "CliSourceApply.cs")])
            .Select(File.ReadAllText)
            .Aggregate(string.Empty, (left, right) => left + "\n" + right);

        foreach (var forbidden in new[]
                 {
                     "SqlConnection",
                     "DbContext",
                     "ControllerBase",
                     "IHostedService",
                     "BackgroundService",
                     "WebApplication",
                     "gh pr create",
                     "\"git\", [\"push\"",
                     "\"git\", [\"commit\"",
                     "\"git\", [\"merge\"",
                     "ReleaseReady",
                     "MergeReady",
                     "DeployReady",
                     "PolicySatisfied = true",
                     "SourceApplied = true"
                 })
        {
            Assert.IsFalse(sourceApplyFiles.Contains(forbidden, StringComparison.OrdinalIgnoreCase), forbidden);
        }
    }

    private static ConscienceDecision AllowDecision() => new()
    {
        DecisionId = "decision-source-apply",
        ActionId = "action-source-apply",
        ActionKind = GovernedActionKind.SourceApply,
        SubjectKind = "SourceApplyRequest",
        SubjectId = "source-apply-request",
        RequestedBy = "human-reviewer",
        EvidenceRefs =
        [
            new ConscienceDecisionEvidenceRef
            {
                RefId = "source-apply-request",
                EvidenceKind = "SourceApplyRequest",
                SafeSummary = "Source apply request evidence."
            }
        ],
        PolicyRefs = ["BlockAF.SourceApply.Forbidden"],
        RiskLevel = ConscienceDecisionRiskLevel.Critical,
        Decision = ConscienceDecisionOutcome.Allow,
        RequiredHumanReview = true,
        ThoughtLedgerRef = "thought-ledger:block-af",
        DecisionHash = string.Empty,
        ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
        CreatedAtUtc = DateTimeOffset.UtcNow
    };

    private static async Task<(int ExitCode, string Output, string Error)> RunCliAsync(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await IronDevCli.RunAsync(args, output, error, CancellationToken.None).ConfigureAwait(false);
        return (exitCode, output.ToString(), error.ToString());
    }

    private static T ReadJson<T>(string path) =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions) ??
        throw new InvalidOperationException($"Could not read {typeof(T).Name} from {path}");

    private static void AssertArtifactExists(string directory, string name) =>
        Assert.IsTrue(File.Exists(Path.Combine(directory, name)), $"{name} should exist in {directory}.");

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "irondev-af-tests", Guid.NewGuid().ToString("N"));
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
            // Best-effort cleanup only.
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private static bool IsSameOrUnderPath(string candidate, string root)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var normalizedCandidate = NormalizeForPathComparison(candidate);
        var normalizedRoot = NormalizeForPathComparison(root);
        return string.Equals(normalizedCandidate, normalizedRoot, comparison) || normalizedCandidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, comparison);
    }

    private static string NormalizeForPathComparison(string path) =>
        Path.GetFullPath(path.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

    private static ProcessResult RunProcess(string fileName, string[] args, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {fileName}.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ProcessResult(process.ExitCode, stdout, stderr);
    }

    private sealed record ProcessResult(int ExitCode, string Stdout, string Stderr);

    private sealed class PatchRunFixture : IDisposable
    {
        private PatchRunFixture(string rootPath, string sourceRepoPath, string runsRoot, string runPath)
        {
            RootPath = rootPath;
            SourceRepoPath = sourceRepoPath;
            RunsRoot = runsRoot;
            RunPath = runPath;
        }

        public string RootPath { get; }
        public string SourceRepoPath { get; }
        public string RunsRoot { get; }
        public string RunPath { get; }

        public static async Task<PatchRunFixture> CreateFinishedRunAsync(string runId)
        {
            var root = CreateTempRoot();
            var sourceRepo = Path.Combine(root, "source");
            var runsRoot = Path.Combine(root, "runs");
            var workspaceRoot = Path.Combine(root, "workspaces");
            var taskFile = Path.Combine(root, "task.md");
            Directory.CreateDirectory(sourceRepo);
            Directory.CreateDirectory(runsRoot);
            Directory.CreateDirectory(workspaceRoot);
            await File.WriteAllTextAsync(Path.Combine(sourceRepo, "README.md"), "Initial IronDev fixture readme.\n").ConfigureAwait(false);
            await File.WriteAllTextAsync(taskFile, "Add a safe Block AF source apply readiness fixture.\n").ConfigureAwait(false);

            AssertGitOk(RunProcess("git", ["init"], sourceRepo), "git init");
            AssertGitOk(RunProcess("git", ["config", "user.email", "block-af@example.test"], sourceRepo), "git config email");
            AssertGitOk(RunProcess("git", ["config", "user.name", "Block AF Test"], sourceRepo), "git config name");
            AssertGitOk(RunProcess("git", ["add", "README.md"], sourceRepo), "git add");
            AssertGitOk(RunProcess("git", ["commit", "-m", "initial"], sourceRepo), "git commit");

            var start = await RunCliAsync(
                "patch",
                "start",
                "--repo",
                sourceRepo,
                "--task",
                taskFile,
                "--test",
                "dotnet --version",
                "--runs-root",
                runsRoot,
                "--workspace-root",
                workspaceRoot,
                "--run-id",
                runId,
                "--json").ConfigureAwait(false);
            Assert.AreEqual(0, start.ExitCode, start.Error + start.Output);

            var runPath = Path.Combine(runsRoot, runId);
            var workspacePath = FindWorkspacePath(Path.Combine(runPath, "run.json"));
            await File.AppendAllTextAsync(Path.Combine(workspacePath, "README.md"), "\nBlock AF source apply readiness fixture change.\n").ConfigureAwait(false);

            var test = await RunCliAsync("patch", "test", "--run", runPath, "--json").ConfigureAwait(false);
            Assert.AreEqual(0, test.ExitCode, test.Error + test.Output);

            var finish = await RunCliAsync("patch", "finish", "--run", runPath, "--skip-test", "--json").ConfigureAwait(false);
            Assert.AreEqual(0, finish.ExitCode, finish.Error + finish.Output);

            return new PatchRunFixture(root, sourceRepo, runsRoot, runPath);
        }

        public void Dispose() => TryDelete(RootPath);

        private static void AssertGitOk(ProcessResult result, string operation) =>
            Assert.AreEqual(0, result.ExitCode, $"{operation} failed: {result.Stderr}{result.Stdout}");

        private static string FindWorkspacePath(string runJsonPath)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(runJsonPath));
            if (TryFindStringProperty(document.RootElement, "workspacePath", out var workspacePath) && Directory.Exists(workspacePath))
                return workspacePath;

            throw new InvalidOperationException("Could not resolve workspace path from run.json.");
        }

        private static bool TryFindStringProperty(JsonElement element, string propertyName, out string value)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
                    {
                        value = property.Value.GetString() ?? string.Empty;
                        return true;
                    }

                    if (TryFindStringProperty(property.Value, propertyName, out value))
                        return true;
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    if (TryFindStringProperty(item, propertyName, out value))
                        return true;
                }
            }

            value = string.Empty;
            return false;
        }
    }
}
