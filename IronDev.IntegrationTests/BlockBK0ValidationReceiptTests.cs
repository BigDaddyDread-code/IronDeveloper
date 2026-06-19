using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Cli;
using IronDev.Core.Validation;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockBK0ValidationReceiptTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [TestMethod]
    public async Task BlockBK0_Receipt_WritesBoundedVerdictAndNoAuthorityClaims()
    {
        var root = CreateTempRoot();
        try
        {
            var lane = ValidationLanePlanner.FindLane("focused-bk0")!;
            var plan = new ValidationLanePlan
            {
                ValidationPlanId = "validation_plan_bk0_test",
                BaseRef = "main",
                HeadRef = "bk0/validation-runtime-hardening",
                CurrentBlock = "BK0",
                ChangedFiles = ["IronDev.Core/Validation/ValidationReceipts.cs"],
                Lanes = [lane],
                Boundary = ValidationRuntimeBoundary.Evidence
            };
            var result = new ValidationProcessResult
            {
                LaneName = "focused-bk0",
                Command = "dotnet",
                Arguments = ["test"],
                WorkingDirectory = root,
                StartedUtc = DateTimeOffset.UtcNow,
                FinishedUtc = DateTimeOffset.UtcNow.AddMilliseconds(25),
                DurationMs = 25,
                ExitCode = 0,
                StdoutPath = Path.Combine(root, "stdout.log"),
                StderrPath = Path.Combine(root, "stderr.log"),
                FailureClassification = ValidationFailureKind.Passed
            };

            var receipt = new ValidationRunReceiptBuilder().Build(plan, [result], "bk0/validation-runtime-hardening", "abc123", true, true);
            var written = await new ValidationReceiptWriter().WriteAsync(root, receipt).ConfigureAwait(false);
            var receiptJson = File.ReadAllText(written.ReceiptPath);
            var summary = File.ReadAllText(written.SummaryPath);

            Assert.AreEqual(ValidationRunVerdict.Passed, receipt.Verdict);
            Assert.AreEqual("focused-bk0", receipt.Results.Single().LaneName);
            StringAssert.Contains(summary, "does not approve, merge, release, deploy, mutate source, promote memory, satisfy policy, or continue workflow");
            AssertDoesNotClaimAuthority(receiptJson);
            AssertDoesNotClaimAuthority(summary);
            Assert.IsTrue(receipt.Boundary.EvidenceOnly);
            Assert.IsFalse(receipt.Boundary.CanMerge);
            Assert.IsFalse(receipt.Boundary.CanRelease);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [TestMethod]
    public void BlockBK0_Receipt_BlocksEnvironmentAndMarksMissingRequiredLaneIncomplete()
    {
        var lane = ValidationLanePlanner.FindLane("restore")!;
        var plan = new ValidationLanePlan
        {
            ValidationPlanId = "validation_plan_blocked",
            ChangedFiles = ["IronDev.Core/IronDev.Core.csproj"],
            Lanes = [lane],
            Boundary = ValidationRuntimeBoundary.Evidence
        };
        var blockedResult = new ValidationProcessResult
        {
            LaneName = "restore",
            Command = "dotnet",
            Arguments = ["restore"],
            WorkingDirectory = Environment.CurrentDirectory,
            StartedUtc = DateTimeOffset.UtcNow,
            FinishedUtc = DateTimeOffset.UtcNow,
            DurationMs = 1,
            ExitCode = 1,
            StdoutPath = "stdout.log",
            StderrPath = "stderr.log",
            FailureClassification = ValidationFailureKind.EnvironmentAccessDenied
        };

        var blocked = new ValidationRunReceiptBuilder().Build(plan, [blockedResult], "branch", "sha", true, true);
        var incomplete = new ValidationRunReceiptBuilder().Build(plan, [], "branch", "sha", true, true, skippedLanes: ["restore"], skippedLaneReasons: ["restore not run"]);

        Assert.AreEqual(ValidationRunVerdict.Blocked, blocked.Verdict);
        Assert.AreEqual(ValidationRunVerdict.Incomplete, incomplete.Verdict);
    }

    [TestMethod]
    public void BlockBK0_Receipt_DirtyAfterAndGeneratedArtifactsCannotPass()
    {
        var lane = ValidationLanePlanner.FindLane("focused-bk0")!;
        var plan = new ValidationLanePlan
        {
            ValidationPlanId = "validation_plan_dirty",
            ChangedFiles = ["IronDev.Core/obj/project.assets.json"],
            Lanes = [lane],
            Boundary = ValidationRuntimeBoundary.Evidence
        };
        var passedResult = new ValidationProcessResult
        {
            LaneName = "focused-bk0",
            Command = "dotnet",
            Arguments = ["test"],
            WorkingDirectory = Environment.CurrentDirectory,
            StartedUtc = DateTimeOffset.UtcNow,
            FinishedUtc = DateTimeOffset.UtcNow,
            DurationMs = 1,
            ExitCode = 0,
            StdoutPath = "stdout.log",
            StderrPath = "stderr.log",
            FailureClassification = ValidationFailureKind.Passed
        };

        var dirtyAfter = new ValidationRunReceiptBuilder().Build(plan, [passedResult], "branch", "sha", true, false);
        var generatedDirty = new ValidationRunReceiptBuilder().Build(
            plan,
            [passedResult],
            "branch",
            "sha",
            true,
            false,
            dirtyChangedFiles: ValidationGeneratedArtifactInspector.FindDirtyGeneratedArtifacts(plan.ChangedFiles));

        Assert.AreEqual(ValidationRunVerdict.Failed, dirtyAfter.Verdict);
        CollectionAssert.Contains(dirtyAfter.FailureClassifications, ValidationFailureKind.UnknownFailure);
        Assert.AreEqual(ValidationRunVerdict.Failed, generatedDirty.Verdict);
        CollectionAssert.Contains(generatedDirty.FailureClassifications, ValidationFailureKind.DirtyGeneratedArtifacts);
        Assert.AreEqual(ValidationChangedFileKind.GeneratedRestoreArtifact, generatedDirty.DirtyChangedFiles.Single().Kind);
    }

    [TestMethod]
    public async Task BlockBK0_Cli_BlocksAuthorityShapedValidationSubcommands()
    {
        foreach (var forbidden in new[] { "approve", "merge", "release", "deploy", "continue", "satisfy-policy", "mutate-source", "promote-memory", "push", "commit", "request-reviewers", "ready", "rerun-ci", "apply", "rollback" })
        {
            var result = await RunCliAsync("validate", forbidden).ConfigureAwait(false);
            Assert.AreEqual(2, result.ExitCode, forbidden);
            StringAssert.Contains(result.Error, "intentionally unsupported");
        }
    }

    [TestMethod]
    public async Task BlockBK0_Cli_BlocksKnownLaneCommandSpoofingAndMissingChangedFileManifest()
    {
        var root = CreateTempRoot();
        try
        {
            var spoof = await RunCliAsync(
                "validate",
                "run",
                "--lane",
                "focused-bk0",
                "--artifacts",
                root,
                "--command",
                "powershell",
                "--arg",
                "-Command",
                "--arg",
                "exit 0").ConfigureAwait(false);
            Assert.AreEqual(2, spoof.ExitCode);
            StringAssert.Contains(spoof.Error, "InvalidLanePlan");
            StringAssert.Contains(spoof.Error, "declared command manifest");

            var missing = await RunCliAsync("validate", "plan", "--changed-files", Path.Combine(root, "missing.txt"), "--json").ConfigureAwait(false);
            Assert.AreEqual(2, missing.ExitCode);
            StringAssert.Contains(missing.Error, "InvalidLanePlan");
            StringAssert.Contains(missing.Error, "changed-files file was not found");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [TestMethod]
    public async Task BlockBK0_Cli_PlanRunReceiptAndInventory_AreEvidenceOnly()
    {
        var root = CreateTempRoot();
        try
        {
            var changedFiles = Path.Combine(root, "changed-files.txt");
            File.WriteAllLines(changedFiles, ["IronDev.Core/Validation/ValidationLanePlanner.cs", "Docs/receipts/BK0_VALIDATION_RUNTIME_AND_TEST_HARNESS_HARDENING.md"]);
            var planPath = Path.Combine(root, "plan.json");

            var planResult = await RunCliAsync("validate", "plan", "--changed-files", changedFiles, "--block", "BK0", "--out", planPath, "--json").ConfigureAwait(false);
            Assert.AreEqual(0, planResult.ExitCode, planResult.Error);
            Assert.IsTrue(File.Exists(planPath));
            var plan = JsonSerializer.Deserialize<ValidationLanePlan>(File.ReadAllText(planPath), JsonOptions)!;
            Assert.IsTrue(plan.Lanes.Any(lane => string.Equals(lane.Name, "focused-bk0", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(plan.Lanes.Any(lane => string.Equals(lane.Name, "fast-authority-invariants", StringComparison.OrdinalIgnoreCase)));

            var runResult = await RunCliAsync(
                "validate",
                "run",
                "--ad-hoc",
                "--artifacts",
                root,
                "--command",
                "powershell",
                "--arg",
                "-NoProfile",
                "--arg",
                "-Command",
                "--arg",
                "Write-Output 'bk0-cli'; exit 0",
                "--timeout-seconds",
                "10",
                "--json").ConfigureAwait(false);
            Assert.AreEqual(0, runResult.ExitCode, runResult.Output + runResult.Error);

            var receiptResult = await RunCliAsync("validate", "receipt", "--artifacts", root, "--last", "--json").ConfigureAwait(false);
            Assert.AreEqual(0, receiptResult.ExitCode, receiptResult.Output + receiptResult.Error);
            StringAssert.Contains(receiptResult.Output, "ad-hoc");
            StringAssert.Contains(receiptResult.Output, "Passed");
            AssertDoesNotClaimAuthority(receiptResult.Output);

            var inventory = await RunCliAsync("validate", "inventory", "--json").ConfigureAwait(false);
            Assert.AreEqual(0, inventory.ExitCode, inventory.Error);
            StringAssert.Contains(inventory.Output, "stable-band");
            StringAssert.Contains(inventory.Output, "dogfood");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [TestMethod]
    public async Task BlockBK0_Cli_DirtyGeneratedArtifactsCannotProducePassedReceipt()
    {
        var root = CreateTempRoot();
        try
        {
            var changedFiles = Path.Combine(root, "changed-files.txt");
            File.WriteAllLines(changedFiles, ["IronDev.Core/obj/project.assets.json"]);

            var runResult = await RunCliAsync(
                "validate",
                "run",
                "--ad-hoc",
                "--artifacts",
                root,
                "--command",
                "powershell",
                "--arg",
                "-NoProfile",
                "--arg",
                "-Command",
                "--arg",
                "exit 0",
                "--changed-files",
                changedFiles,
                "--json").ConfigureAwait(false);
            Assert.AreEqual(1, runResult.ExitCode, runResult.Output + runResult.Error);

            var receiptResult = await RunCliAsync("validate", "receipt", "--artifacts", root, "--last", "--json").ConfigureAwait(false);
            Assert.AreEqual(1, receiptResult.ExitCode, receiptResult.Output + receiptResult.Error);
            StringAssert.Contains(receiptResult.Output, "DirtyGeneratedArtifacts");
            StringAssert.Contains(receiptResult.Output, "GeneratedRestoreArtifact");
            StringAssert.Contains(receiptResult.Output, "Failed");
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static void AssertDoesNotClaimAuthority(string value)
    {
        Assert.IsFalse(value.Contains("ReadyToMerge", StringComparison.Ordinal), value);
        Assert.IsFalse(value.Contains("ReadyToRelease", StringComparison.Ordinal), value);
        Assert.IsFalse(value.Contains("PolicySatisfied", StringComparison.Ordinal), value);
        Assert.IsFalse(value.Contains("Approved", StringComparison.Ordinal), value);
        Assert.IsFalse(value.Contains("CreatePullRequest", StringComparison.Ordinal), value);
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunCliAsync(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await IronDevCli.RunAsync(args, output, error, CancellationToken.None).ConfigureAwait(false);
        return (exitCode, output.ToString(), error.ToString());
    }

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "irondev-bk0-receipt-" + Guid.NewGuid().ToString("N"));
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
}
