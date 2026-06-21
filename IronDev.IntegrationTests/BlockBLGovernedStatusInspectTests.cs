using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Cli;
using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockBLGovernedStatusInspectTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [TestMethod]
    public void BlockBL_Inspect_BlockedStatus_ReturnsValidReadModel()
    {
        var result = Inspect(BlockedStatus());

        AssertValid(result);
        StringAssert.Contains(result.Summary, "valid SourceApply Blocked status");
        AssertContains(result.BlockedReasonLines, "Accepted source-apply request is missing.");
        AssertContains(result.MissingEvidenceLines, "accepted-source-apply-request:source-apply-123");
        AssertContains(result.NextSafeActionLines, "request accepted source-apply authority for patch hash patchhash-abc (guidance only)");
        AssertContains(result.ForbiddenActionLines, "do not apply blocked source apply status");
    }

    [TestMethod]
    public void BlockBL_Inspect_EligibleStatus_ReturnsValidReadModel()
    {
        var result = Inspect(EligibleStatus());

        AssertValid(result);
        AssertContains(result.NextSafeActionLines, "request controlled source apply execution for patch hash patchhash-abc (guidance only)");
        AssertContains(result.BoundaryLines, "eligible status is explanation, not execution authority");
    }

    [TestMethod]
    public void BlockBL_Inspect_RunningStatus_ReturnsValidReadModel()
    {
        var result = Inspect(RunningStatus());

        AssertValid(result);
        Assert.AreEqual(GovernedOperationState.Running, result.Status.State);
        AssertContains(result.ForbiddenActionLines, "do not continue workflow from running source apply status");
    }

    [TestMethod]
    public void BlockBL_Inspect_CompletedStatus_ReturnsValidReadModel()
    {
        var result = Inspect(CompletedStatus());

        AssertValid(result);
        AssertContains(result.ReceiptRefLines, "source-apply-receipt:receipt-123 (reference only)");
        AssertContains(result.BoundaryLines, "completed status is not authority for the next governed operation");
    }

    [TestMethod]
    public void BlockBL_Inspect_FailedStatus_ReturnsValidReadModel()
    {
        var result = Inspect(FailedStatus());

        AssertValid(result);
        Assert.AreEqual(GovernedOperationState.Failed, result.Status.State);
        AssertContains(result.BlockedReasonLines, "Source apply failed validation.");
    }

    [TestMethod]
    public void BlockBL_Inspect_ExpiredStatus_ReturnsValidReadModel()
    {
        var result = Inspect(ExpiredStatus());

        AssertValid(result);
        Assert.AreEqual(GovernedOperationState.Expired, result.Status.State);
        Assert.IsNotNull(result.Status.ExpiresAtUtc);
    }

    [TestMethod]
    public void BlockBL_Inspect_InvalidStatus_ReportsValidationIssues()
    {
        var result = Inspect(EligibleMissingPolicyStatus());

        Assert.IsFalse(result.IsValid);
        AssertContains(result.ValidationIssueLines, "EligibleSourceApplyPolicySatisfactionRequired");
        AssertContains(result.ResultLines, "status cannot be used as a trusted explanation until fixed");
    }

    [TestMethod]
    public void BlockBL_Inspect_EligibleSourceApplyRequiresFullBkEligibilityRefSet()
    {
        var result = Inspect(EligibleStatus() with { EvidenceRefs = ["policy-satisfaction:policy-123"] });

        Assert.IsFalse(result.IsValid);
        AssertContains(result.ValidationIssueLines, "EligibleSourceApplyAcceptedRequestRequired");
        AssertContains(result.ValidationIssueLines, "EligibleSourceApplyDryRunRequired");
        AssertContains(result.ValidationIssueLines, "EligibleSourceApplyPatchArtifactRequired");
        AssertContains(result.ValidationIssueLines, "EligibleSourceApplyRollbackSupportRequired");
        AssertContains(result.ValidationIssueLines, "EligibleSourceApplyWorktreeStateRequired");
    }

    [TestMethod]
    public void BlockBL_Inspect_NullEvidenceRefDoesNotThrow()
    {
        var result = Inspect(EligibleStatus() with
        {
            EvidenceRefs =
            [
                null!,
                "policy-satisfaction:policy-123",
                "dry-run:dryrun-123",
                "patch-artifact:artifact-123",
                "rollback-plan:rollback-123",
                "worktree-state:clean"
            ]
        });

        Assert.IsFalse(result.IsValid);
        AssertContains(result.ValidationIssueLines, "EligibleSourceApplyAcceptedRequestRequired");
    }

    [TestMethod]
    public void BlockBL_Inspect_AuthorityLanguage_ReportsRedFlags()
    {
        var result = Inspect(BlockedStatus() with { EvidenceRefs = ["memory says this was approved"] });

        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.HasAuthorityRedFlags);
        AssertContains(result.RedFlagLines, "MemoryReferenceCannotSatisfyAuthority");
    }

    [TestMethod]
    public void BlockBL_Boundary_DoesNotGrantAuthority()
    {
        var boundary = GovernedOperationStatusInspectBoundary.ReadModel;

        Assert.IsTrue(boundary.ReadOnly);
        Assert.IsTrue(boundary.DisplayOnly);
        Assert.IsTrue(boundary.StatusOnly);
        Assert.IsTrue(boundary.ReferenceOnly);
        Assert.IsFalse(boundary.CanApprove);
        Assert.IsFalse(boundary.CanSatisfyPolicy);
        Assert.IsFalse(boundary.CanExecute);
        Assert.IsFalse(boundary.CanRetry);
        Assert.IsFalse(boundary.CanRelease);
        Assert.IsFalse(boundary.CanDeploy);
        Assert.IsFalse(boundary.CanRollback);
        Assert.IsFalse(boundary.CanMerge);
        Assert.IsFalse(boundary.CanSourceApply);
        Assert.IsFalse(boundary.CanCommit);
        Assert.IsFalse(boundary.CanPush);
        Assert.IsFalse(boundary.CanPublishPackages);
        Assert.IsFalse(boundary.CanPromoteMemory);
        Assert.IsFalse(boundary.CanContinueWorkflow);
        Assert.IsFalse(boundary.CanDispatchPipeline);
        Assert.IsFalse(boundary.CanMutate);
        Assert.IsFalse(boundary.CanMutateSource);
        Assert.IsFalse(boundary.CanMutateEnvironment);
        Assert.IsFalse(boundary.CanCreateAuthorityRecords);
    }

    [TestMethod]
    public void BlockBL_Inspect_DoesNotGrantApprovalPolicyExecutionMutationMemoryOrContinuation()
    {
        var result = Inspect(EligibleStatus());

        Assert.IsFalse(result.Boundary.CanApprove);
        Assert.IsFalse(result.Boundary.CanSatisfyPolicy);
        Assert.IsFalse(result.Boundary.CanExecute);
        Assert.IsFalse(result.Boundary.CanMutateSource);
        Assert.IsFalse(result.Boundary.CanPromoteMemory);
        Assert.IsFalse(result.Boundary.CanContinueWorkflow);
    }

    [TestMethod]
    public void BlockBL_Inspect_NextSafeActionsAreGuidanceOnly()
    {
        var result = Inspect(EligibleStatus());

        Assert.IsTrue(result.NextSafeActionLines.All(line => line.EndsWith("(guidance only)", StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(result.Boundary.CanExecute);
    }

    [TestMethod]
    public void BlockBL_Inspect_EvidenceRefsAreReferencesOnly()
    {
        var result = Inspect(EligibleStatus());

        Assert.IsTrue(result.EvidenceRefLines.Any(line => line.Contains("policy-satisfaction:policy-123", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(result.EvidenceRefLines.All(line => line.EndsWith("(reference only)", StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(result.Boundary.CanSatisfyPolicy);
    }

    [TestMethod]
    public void BlockBL_Inspect_ReceiptRefsAreReferencesOnly()
    {
        var result = Inspect(CompletedStatus());

        Assert.IsTrue(result.ReceiptRefLines.All(line => line.EndsWith("(reference only)", StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(result.Boundary.CanContinueWorkflow);
    }

    [TestMethod]
    public async Task BlockBL_Cli_ReadsStatusJsonAndReturnsZeroForValidStatus()
    {
        var statusPath = await WriteStatusAsync(EligibleStatus()).ConfigureAwait(false);
        var result = await RunCliAsync("operation-status", "inspect", "--status", statusPath).ConfigureAwait(false);

        Assert.AreEqual(0, result.ExitCode, result.Output + result.Error);
        StringAssert.Contains(result.Output, "Operation: SourceApply");
        StringAssert.Contains(result.Output, "Validation:");
        StringAssert.Contains(result.Output, "- valid");
        StringAssert.Contains(result.Output, "eligible status is explanation, not execution authority");
    }

    [TestMethod]
    public async Task BlockBL_Cli_ReturnsOneForInvalidStatus()
    {
        var statusPath = await WriteStatusAsync(EligibleMissingPolicyStatus()).ConfigureAwait(false);
        var result = await RunCliAsync("operation-status", "inspect", "--status", statusPath).ConfigureAwait(false);

        Assert.AreEqual(1, result.ExitCode, result.Output + result.Error);
        StringAssert.Contains(result.Output, "EligibleSourceApplyPolicySatisfactionRequired");
        StringAssert.Contains(result.Output, "status cannot be used as a trusted explanation until fixed");
    }

    [TestMethod]
    public async Task BlockBL_Cli_ReturnsTwoForMalformedJson()
    {
        var statusPath = Path.Combine(TempDir("bl-malformed"), "operation-status.json");
        Directory.CreateDirectory(Path.GetDirectoryName(statusPath)!);
        await File.WriteAllTextAsync(statusPath, "{ not valid json").ConfigureAwait(false);

        var result = await RunCliAsync("operation-status", "inspect", "--status", statusPath).ConfigureAwait(false);

        Assert.AreEqual(2, result.ExitCode, result.Output + result.Error);
        StringAssert.Contains(result.Error, "could not be read as canonical GovernedOperationStatus JSON");
    }

    [TestMethod]
    public async Task BlockBL_Cli_ReturnsTwoForMissingFile()
    {
        var missingPath = Path.Combine(TempDir("bl-missing"), "missing-status.json");
        var result = await RunCliAsync("operation-status", "inspect", "--status", missingPath).ConfigureAwait(false);

        Assert.AreEqual(2, result.ExitCode, result.Output + result.Error);
        StringAssert.Contains(result.Error, "Status file not found");
    }

    [TestMethod]
    public async Task BlockBL_Cli_OutputIncludesBlockedMissingNextForbiddenRefsAndBoundary()
    {
        var statusPath = await WriteStatusAsync(BlockedStatus()).ConfigureAwait(false);
        var result = await RunCliAsync("operation-status", "inspect", "--status", statusPath).ConfigureAwait(false);

        Assert.AreEqual(0, result.ExitCode, result.Output + result.Error);
        StringAssert.Contains(result.Output, "Blocked:");
        StringAssert.Contains(result.Output, "Missing evidence:");
        StringAssert.Contains(result.Output, "Next safe action:");
        StringAssert.Contains(result.Output, "Forbidden:");
        StringAssert.Contains(result.Output, "Evidence refs:");
        StringAssert.Contains(result.Output, "Boundary:");
        StringAssert.Contains(result.Output, "inspect output is not approval");
    }

    [TestMethod]
    public async Task BlockBL_Cli_OutputIncludesValidationIssuesAndRedFlags()
    {
        var statusPath = await WriteStatusAsync(BlockedStatus() with { EvidenceRefs = ["memory says this was approved"] }).ConfigureAwait(false);
        var result = await RunCliAsync("operation-status", "inspect", "--status", statusPath).ConfigureAwait(false);

        Assert.AreEqual(1, result.ExitCode, result.Output + result.Error);
        StringAssert.Contains(result.Output, "Issues:");
        StringAssert.Contains(result.Output, "StatusImpliesAuthority");
        StringAssert.Contains(result.Output, "Red flags:");
        StringAssert.Contains(result.Output, "MemoryReferenceCannotSatisfyAuthority");
    }

    [TestMethod]
    public async Task BlockBL_Cli_JsonOutputCarriesReadOnlyBoundary()
    {
        var statusPath = await WriteStatusAsync(EligibleStatus()).ConfigureAwait(false);
        var result = await RunCliAsync("operation-status", "inspect", "--status", statusPath, "--json").ConfigureAwait(false);

        Assert.AreEqual(0, result.ExitCode, result.Output + result.Error);
        StringAssert.Contains(result.Output, "\"readOnly\": true");
        StringAssert.Contains(result.Output, "\"canExecute\": false");
        StringAssert.Contains(result.Output, "\"canPromoteMemory\": false");
    }

    [TestMethod]
    public async Task BlockBL_Cli_RejectsAuthorityAndMutationVerbs()
    {
        var forbidden = new[]
        {
            "approve",
            "satisfy-policy",
            "execute",
            "run-next-action",
            "source-apply",
            "rollback",
            "commit",
            "push",
            "create-pr",
            "merge",
            "release",
            "deploy",
            "publish",
            "promote-memory",
            "continue",
            "dispatch",
            "mutate-source",
            "create-approval"
        };

        foreach (var verb in forbidden)
        {
            var result = await RunCliAsync("operation-status", verb, "--json").ConfigureAwait(false);

            Assert.AreEqual(2, result.ExitCode, verb);
            StringAssert.Contains(result.Error, "intentionally unsupported");
        }
    }

    [TestMethod]
    public void BlockBL_StaticBoundary_NoMutationProviderOrExecutorSurface()
    {
        var root = FindRepositoryRoot();
        var files = new[]
        {
            Path.Combine(root, "IronDev.Core", "Governance", "GovernedOperationStatusInspectModels.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "GovernedOperationStatusInspector.cs"),
            Path.Combine(root, "tools", "IronDev.Cli", "CliGovernedOperationStatusInspect.cs")
        };
        var text = string.Join(Environment.NewLine, files.Select(File.ReadAllText));
        var forbidden = new[]
        {
            "RunProcessAsync",
            "ProcessStartInfo",
            "git apply",
            "git commit",
            "git push",
            "gh pr create",
            "gh api",
            "kubectl",
            "terraform apply",
            "docker push",
            "npm publish",
            "source apply execute",
            "rollback execute",
            "commit execute",
            "push execute",
            "merge execute",
            "release execute",
            "deploy execute",
            "promote memory",
            "continue workflow"
        };

        foreach (var value in forbidden)
            Assert.IsFalse(text.Contains(value, StringComparison.OrdinalIgnoreCase), value);
    }

    [TestMethod]
    public void BlockBL_Receipt_RecordsInspectBoundary()
    {
        var doc = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Docs", "receipts", "BL_GOVERNED_STATUS_INSPECT.md"));

        StringAssert.Contains(doc, "This slice adds a read-only inspect surface for canonical GovernedOperationStatus.");
        StringAssert.Contains(doc, "It validates status through GovernedOperationStatusValidator.");
        StringAssert.Contains(doc, "It does not approve.");
        StringAssert.Contains(doc, "It does not satisfy policy.");
        StringAssert.Contains(doc, "It does not execute.");
        StringAssert.Contains(doc, "It does not mutate source.");
        StringAssert.Contains(doc, "It does not promote memory.");
        StringAssert.Contains(doc, "It does not continue workflow.");
        StringAssert.Contains(doc, "Eligible SourceApply inspect validation requires accepted source-apply request, policy satisfaction, dry-run, patch artifact, rollback-plan, and worktree-state refs.");
        StringAssert.Contains(doc, "NextSafeActions are displayed as guidance only.");
        StringAssert.Contains(doc, "Inspect output is not authority.");
        StringAssert.Contains(doc, "Inspect can read the sign on the locked door. It cannot open the door.");
    }

    private static GovernedOperationStatusInspectResult Inspect(GovernedOperationStatus status) =>
        GovernedOperationStatusInspector.Inspect(new GovernedOperationStatusInspectRequest { Status = status });

    private static GovernedOperationStatus BlockedStatus() =>
        BaseStatus("source-apply-status-blocked", GovernedOperationState.Blocked) with
        {
            BlockedReasons = ["Accepted source-apply request is missing."],
            MissingEvidence = ["accepted-source-apply-request:source-apply-123"],
            NextSafeActions = ["request accepted source-apply authority for patch hash patchhash-abc"],
            ForbiddenActions = ["do not apply blocked source apply status"],
            EvidenceRefs = ["patch-proposal:proposal-123", "patch-hash:patchhash-abc"]
        };

    private static GovernedOperationStatus EligibleStatus() =>
        BaseStatus("source-apply-status-eligible", GovernedOperationState.Eligible) with
        {
            NextSafeActions = ["request controlled source apply execution for patch hash patchhash-abc"],
            ForbiddenActions = ["do not treat status as execution authority", "do not apply from status alone"],
            EvidenceRefs =
            [
                "accepted-source-apply-request:source-apply-123",
                "policy-satisfaction:policy-123",
                "dry-run:dryrun-123",
                "patch-artifact:artifact-123",
                "rollback-plan:rollback-123",
                "worktree-state:clean"
            ]
        };

    private static GovernedOperationStatus EligibleMissingPolicyStatus() =>
        EligibleStatus() with
        {
            EvidenceRefs = EligibleStatus().EvidenceRefs.Where(value => !value.StartsWith("policy-satisfaction:", StringComparison.OrdinalIgnoreCase)).ToArray()
        };

    private static GovernedOperationStatus RunningStatus() =>
        BaseStatus("source-apply-status-running", GovernedOperationState.Running) with
        {
            NextSafeActions = ["inspect source apply run progress"],
            ForbiddenActions = ["do not continue workflow from running source apply status"],
            EvidenceRefs = ["accepted-source-apply-request:source-apply-123", "executor-run:run-123"]
        };

    private static GovernedOperationStatus CompletedStatus() =>
        BaseStatus("source-apply-status-completed", GovernedOperationState.Completed) with
        {
            NextSafeActions = ["review source apply receipt before requesting controlled commit package"],
            ForbiddenActions = ["do not treat source apply completion as commit authority"],
            ReceiptRefs = ["source-apply-receipt:receipt-123"]
        };

    private static GovernedOperationStatus FailedStatus() =>
        BaseStatus("source-apply-status-failed", GovernedOperationState.Failed) with
        {
            BlockedReasons = ["Source apply failed validation."],
            NextSafeActions = ["review failure receipt and prepare a new governed proposal"],
            ForbiddenActions = ["do not retry source apply without fresh authority"],
            ReceiptRefs = ["source-apply-failure-receipt:failure-123"]
        };

    private static GovernedOperationStatus ExpiredStatus() =>
        BaseStatus("source-apply-status-expired", GovernedOperationState.Expired) with
        {
            BlockedReasons = ["Accepted source apply request expired."],
            NextSafeActions = ["request fresh source apply authority for patch hash patchhash-abc"],
            ForbiddenActions = ["do not reuse old apply request"],
            ExpiresAtUtc = DateTimeOffset.Parse("2026-06-21T03:00:00Z")
        };

    private static GovernedOperationStatus BaseStatus(string id, GovernedOperationState state) =>
        new()
        {
            OperationId = id,
            OperationKind = "SourceApply",
            Subject = "repo:BigDaddyDread-code/IronDeveloper branch:main patch:patchhash-abc",
            State = state,
            BlockedReasons = [],
            MissingEvidence = [],
            NextSafeActions = [],
            ForbiddenActions = [],
            EvidenceRefs = [],
            ReceiptRefs = [],
            ObservedAtUtc = DateTimeOffset.Parse("2026-06-21T04:00:00Z")
        };

    private static void AssertValid(GovernedOperationStatusInspectResult result)
    {
        Assert.IsTrue(result.IsValid, string.Join(", ", result.ValidationIssueLines.Concat(result.RedFlagLines)));
        Assert.IsFalse(result.HasAuthorityRedFlags);
    }

    private static void AssertContains(IReadOnlyList<string> values, string expected) =>
        Assert.IsTrue(values.Contains(expected, StringComparer.OrdinalIgnoreCase), string.Join(", ", values));

    private static async Task<string> WriteStatusAsync(GovernedOperationStatus status)
    {
        var path = Path.Combine(TempDir("bl-status"), "operation-status.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(status, JsonOptions)).ConfigureAwait(false);
        return path;
    }

    private static string TempDir(string prefix) =>
        Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");

    private static async Task<(int ExitCode, string Output, string Error)> RunCliAsync(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await IronDevCli.RunAsync(args, output, error, CancellationToken.None).ConfigureAwait(false);
        return (exitCode, output.ToString(), error.ToString());
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

        throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
