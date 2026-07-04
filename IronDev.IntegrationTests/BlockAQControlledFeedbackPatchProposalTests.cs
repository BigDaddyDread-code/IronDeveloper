using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Cli;
using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockAQControlledFeedbackPatchProposalTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [TestMethod]
    public void BlockAQ_Builder_CreatesAttributedProposalWithoutSourceAuthority()
    {
        var package = CreatePackage(
            Candidate("remediate-code", FeedbackDisposition.Remediate, false, "IronDev.Core/Feedback/Foo.cs"),
            Candidate("needs-human", FeedbackDisposition.Remediate, true, "IronDev.Core/Feedback/Human.cs"),
            Candidate("do-not", FeedbackDisposition.DoNotRemediate, false, "Docs/notes.md"));

        var artifacts = FeedbackPatchProposalBuilder.Build(new FeedbackPatchProposalInput
        {
            Package = package,
            ExpectedPrNumber = package.PullRequestNumber,
            ExpectedHeadSha = package.CurrentHeadSha,
            BaseSha = new string('b', 40)
        });

        var proposal = artifacts.Proposal;
        Assert.AreEqual(FeedbackPatchProposalVerdict.Incomplete, proposal.Verdict);
        Assert.AreEqual(1, proposal.ProposedFiles.Length);
        Assert.AreEqual("IronDev.Core/Feedback/Foo.cs", proposal.ExpectedChangedFiles.Single());
        Assert.AreEqual(1, proposal.ProposedHunks.Length);
        var hunk = proposal.ProposedHunks.Single();
        CollectionAssert.Contains(hunk.RemediationCandidateIds, "remediate-code");
        Assert.AreEqual(FeedbackPatchApplicability.ManualReviewOnly, hunk.PatchApplicability);
        Assert.IsFalse(string.IsNullOrWhiteSpace(hunk.TargetLineHint));
        Assert.IsFalse(string.IsNullOrWhiteSpace(hunk.OriginalContext));
        Assert.IsFalse(string.IsNullOrWhiteSpace(hunk.ProposedReplacement));
        Assert.IsFalse(string.IsNullOrWhiteSpace(hunk.ProposalText));
        StringAssert.Contains(hunk.ProposalText, "Manual review proposal");
        Assert.IsFalse(proposal.ProposedHunks.Any(item => item.RemediationCandidateIds.Contains("needs-human")));
        Assert.IsTrue(proposal.IncompleteReasons.Any(item => item.Contains("CandidateRequiresHumanDecision:needs-human", StringComparison.Ordinal)));
        Assert.IsTrue(proposal.IncompleteReasons.Any(item => item.Contains("CandidateNotRemediate:do-not:DoNotRemediate", StringComparison.Ordinal)));
        AssertBoundary(proposal.Boundary);
        AssertBoundary(artifacts.Receipt.Boundary);
        Assert.IsFalse(FeedbackPatchProposalBypassEvaluator.CanApplySource(proposal));
        Assert.IsFalse(FeedbackPatchProposalBypassEvaluator.CanUpdatePullRequest(proposal));
    }

    [TestMethod]
    public void BlockAQ_Builder_FailsClosedForWrongOrStalePackageBinding()
    {
        var package = CreatePackage(Candidate("remediate-code", FeedbackDisposition.Remediate, false, "IronDev.Core/Feedback/Foo.cs"));

        var wrongPr = FeedbackPatchProposalBuilder.Build(new FeedbackPatchProposalInput
        {
            Package = package,
            ExpectedPrNumber = 999,
            ExpectedHeadSha = package.CurrentHeadSha
        }).Proposal;
        Assert.AreEqual(FeedbackPatchProposalVerdict.Rejected, wrongPr.Verdict);
        Assert.IsTrue(wrongPr.CannotApplyReasons.Contains("FeedbackPackagePrMismatch"));

        var stale = FeedbackPatchProposalBuilder.Build(new FeedbackPatchProposalInput
        {
            Package = package,
            ExpectedPrNumber = package.PullRequestNumber,
            ExpectedHeadSha = new string('c', 40)
        }).Proposal;
        Assert.AreEqual(FeedbackPatchProposalVerdict.Rejected, stale.Verdict);
        Assert.IsTrue(stale.CannotApplyReasons.Contains("FeedbackPackageHeadMismatch"));
    }

    [TestMethod]
    public void BlockAQ_Builder_BlocksUnsafeGeneratedAndUnflaggedGovernanceFiles()
    {
        var package = CreatePackage(
            Candidate("generated", FeedbackDisposition.Remediate, false, "IronDev.Core/obj/project.assets.json"),
            Candidate("governance-unflagged", FeedbackDisposition.Remediate, false, "IronDev.Core/Governance/GovernedActionKernel.cs"),
            Candidate("governance-flagged", FeedbackDisposition.Remediate, false, "IronDev.Core/Governance/GovernedActionKernel.cs", authorityRisk: true));

        var proposal = FeedbackPatchProposalBuilder.Build(new FeedbackPatchProposalInput { Package = package }).Proposal;

        Assert.AreEqual(FeedbackPatchProposalVerdict.Rejected, proposal.Verdict);
        Assert.IsTrue(proposal.CannotApplyReasons.Any(item => item.Contains("GeneratedFileCannotBeProposed:generated", StringComparison.Ordinal)));
        Assert.IsTrue(proposal.CannotApplyReasons.Any(item => item.Contains("GovernanceFileRequiresAuthorityRisk:governance-unflagged", StringComparison.Ordinal)));
        Assert.IsTrue(proposal.ProposedFiles.Any(item => item.RemediationCandidateIds.Contains("governance-flagged") && item.AuthorityRisk));
        Assert.IsTrue(proposal.ProposedHunks.All(item => item.RemediationCandidateIds.Length > 0));
        AssertBoundary(proposal.Boundary);
    }

    [TestMethod]
    public void BlockAQ_Builder_BlocksAbsoluteTraversalAndEnvironmentShapedPaths()
    {
        var package = CreatePackage(
            Candidate("windows-drive", FeedbackDisposition.Remediate, false, @"C:\Workspaces\file.cs"),
            Candidate("drive-forward", FeedbackDisposition.Remediate, false, "D:/repo/file.cs"),
            Candidate("unc", FeedbackDisposition.Remediate, false, @"\\server\share\file.cs"),
            Candidate("rooted", FeedbackDisposition.Remediate, false, "/tmp/file.cs"),
            Candidate("traversal", FeedbackDisposition.Remediate, false, "../file.cs"),
            Candidate("home", FeedbackDisposition.Remediate, false, "~/file.cs"),
            Candidate("percent-env", FeedbackDisposition.Remediate, false, "%USERPROFILE%/file.cs"),
            Candidate("dollar-env", FeedbackDisposition.Remediate, false, "$HOME/file.cs"));

        var proposal = FeedbackPatchProposalBuilder.Build(new FeedbackPatchProposalInput { Package = package }).Proposal;

        Assert.AreEqual(FeedbackPatchProposalVerdict.Rejected, proposal.Verdict);
        Assert.AreEqual(0, proposal.ProposedFiles.Length);
        Assert.AreEqual(0, proposal.ProposedHunks.Length);
        AssertUnsafePath(proposal, "windows-drive");
        AssertUnsafePath(proposal, "drive-forward");
        AssertUnsafePath(proposal, "unc");
        AssertUnsafePath(proposal, "rooted");
        AssertUnsafePath(proposal, "traversal");
        AssertUnsafePath(proposal, "home");
        AssertUnsafePath(proposal, "percent-env");
        AssertUnsafePath(proposal, "dollar-env");
        AssertBoundary(proposal.Boundary);
    }

    [TestMethod]
    public async Task BlockAQ_Cli_ProposeInspectStatusRecords_WithoutMutatingSource()
    {
        var root = CreateTempRoot();
        try
        {
            var sourcePath = Path.Combine(root, "source.txt");
            File.WriteAllText(sourcePath, "original");
            var packagePath = Path.Combine(root, "feedback-remediation-package.json");
            var package = CreatePackage(Candidate("remediate-code", FeedbackDisposition.Remediate, false, "IronDev.Core/Feedback/Foo.cs"));
            WriteJson(packagePath, package);
            var outPath = Path.Combine(root, "proposal");

            var result = await RunCliAsync("feedback-patch", "propose", "--package", packagePath, "--out", outPath, "--pr", package.PullRequestNumber.ToString(), "--head", package.CurrentHeadSha, "--json").ConfigureAwait(false);
            Assert.AreEqual(0, result.ExitCode, result.Output + result.Error);
            Assert.AreEqual("original", File.ReadAllText(sourcePath));
            Assert.IsTrue(File.Exists(Path.Combine(outPath, "feedback-patch-proposal.json")));
            Assert.IsTrue(File.Exists(Path.Combine(outPath, "feedback-patch-proposal-notes.md")));
            Assert.IsFalse(File.Exists(Path.Combine(outPath, "feedback-patch-proposal.diff")));
            Assert.IsTrue(File.Exists(Path.Combine(outPath, "feedback-patch-proposal-summary.md")));
            Assert.IsTrue(File.Exists(Path.Combine(outPath, "feedback-patch-proposal-receipt.json")));

            var proposal = ReadJson<FeedbackPatchProposal>(Path.Combine(outPath, "feedback-patch-proposal.json"));
            Assert.AreEqual(FeedbackPatchProposalVerdict.ProposalCreated, proposal!.Verdict);
            AssertBoundary(proposal.Boundary);
            var notes = File.ReadAllText(Path.Combine(outPath, "feedback-patch-proposal-notes.md"));
            StringAssert.Contains(notes, "ManualReviewOnly");
            Assert.IsFalse(notes.Contains("diff --git", StringComparison.OrdinalIgnoreCase));
            var inspect = await RunCliAsync("feedback-patch", "inspect", "--proposal", Path.Combine(outPath, "feedback-patch-proposal.json"), "--json").ConfigureAwait(false);
            Assert.AreEqual(0, inspect.ExitCode, inspect.Output + inspect.Error);
            var status = await RunCliAsync("feedback-patch", "status", "--proposal", Path.Combine(outPath, "feedback-patch-proposal.json")).ConfigureAwait(false);
            Assert.AreEqual(0, status.ExitCode, status.Output + status.Error);
            var records = await RunCliAsync("feedback-patch", "records", "--proposal", Path.Combine(outPath, "feedback-patch-proposal.json")).ConfigureAwait(false);
            Assert.AreEqual(0, records.ExitCode, records.Output + records.Error);
            StringAssert.Contains(records.Output, "IronDev.Core/Feedback/Foo.cs");

            var missing = await RunCliAsync("feedback-patch", "propose", "--package", Path.Combine(root, "missing.json"), "--out", outPath).ConfigureAwait(false);
            Assert.AreEqual(1, missing.ExitCode);
            StringAssert.Contains(missing.Error, "feedback remediation package not found");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [TestMethod]
    public async Task BlockAQ_Cli_BlocksAuthorityShapedSubcommands()
    {
        foreach (var forbidden in new[] { "apply", "commit", "push", "update-pr", "ready", "request-reviewers", "approve", "merge", "release", "deploy", "continue" })
        {
            var result = await RunCliAsync("feedback-patch", forbidden, "--proposal", "proposal.json").ConfigureAwait(false);
            Assert.AreEqual(2, result.ExitCode, forbidden);
            StringAssert.Contains(result.Error, "intentionally unsupported");
        }
    }

    [TestMethod]
    public void BlockAQ_StaticBoundaryAndReceipt_ProveNoSourceOrPrMutationSurface()
    {
        var root = FindRepositoryRoot();
        var cli = File.ReadAllText(Path.Combine(root, "tools", "IronDev.Cli", "CliFeedbackPatch.cs"));
        Assert.IsFalse(cli.Contains("RunProcessAsync", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("\"git\"", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("\"gh\"", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("merge_pull_request", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("enable_auto_merge", StringComparison.OrdinalIgnoreCase));

        var receipt = File.ReadAllText(Path.Combine(root, "Docs", "receipts", "AQ_CONTROLLED_FEEDBACK_PATCH_PROPOSAL.md"));
        StringAssert.Contains(receipt, "AQ1 AP package required.");
        StringAssert.Contains(receipt, "AQ2 candidate eligibility.");
        StringAssert.Contains(receipt, "AQ3 hunk attribution.");
        StringAssert.Contains(receipt, "AQ4 patch safety checks.");
        StringAssert.Contains(receipt, "AQ5 evidence-only proposal receipt.");
        StringAssert.Contains(receipt, "AQ6 authority bypass tests.");
        StringAssert.Contains(receipt, "Patch proposal is not source apply.");
        StringAssert.Contains(receipt, "It does not apply source changes.");
        StringAssert.Contains(receipt, "It does not update PR branches.");
        StringAssert.Contains(receipt, "It does not continue workflow.");
    }

    private static void AssertUnsafePath(FeedbackPatchProposal proposal, string remediationId) =>
        Assert.IsTrue(proposal.CannotApplyReasons.Any(item => item.StartsWith($"UnsafePatchFile:{remediationId}:", StringComparison.Ordinal)), remediationId);

    private static FeedbackRemediationPackage CreatePackage(params FeedbackRemediationCandidate[] candidates) => new()
    {
        FeedbackRemediationPackageId = "feedback_pkg_aq",
        RunId = "run-aq",
        RepositoryFullName = "owner/repo",
        PullRequestNumber = 462,
        CurrentHeadSha = new string('a', 40),
        PullRequestUrl = "https://github.com/owner/repo/pull/462",
        FeedbackItems = [],
        RemediationCandidates = candidates,
        EvidenceRefs = ["feedback-remediation-package.json"],
        ValidationExpectations = candidates.SelectMany(item => item.SuggestedValidationLanes).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
        CreatedAtUtc = DateTimeOffset.Parse("2026-06-20T00:00:00Z")
    };

    private static FeedbackRemediationCandidate Candidate(string id, FeedbackDisposition disposition, bool requiresHumanDecision, string filePath, bool authorityRisk = false) => new()
    {
        RemediationId = id,
        FeedbackItemIds = [$"item-{id}"],
        Disposition = disposition,
        Rationale = $"Rationale for {id}.",
        AffectedAreas = [filePath.Contains("Governance", StringComparison.OrdinalIgnoreCase) ? "governance" : "source"],
        LikelyFiles = [filePath],
        RiskLevel = authorityRisk ? FeedbackRiskLevel.High : FeedbackRiskLevel.Low,
        AuthorityRisk = authorityRisk,
        SuggestedValidationLanes = authorityRisk ? ["FastAuthorityInvariant", "Build"] : ["FocusedCurrentBlock"],
        RequiresHumanDecision = requiresHumanDecision
    };

    private static void AssertBoundary(FeedbackPatchProposalBoundary boundary)
    {
        Assert.IsTrue(boundary.EvidenceOnly);
        Assert.IsFalse(boundary.CanApplySource);
        Assert.IsFalse(boundary.CanMutateWorkspace);
        Assert.IsFalse(boundary.CanCommit);
        Assert.IsFalse(boundary.CanPush);
        Assert.IsFalse(boundary.CanUpdatePullRequest);
        Assert.IsFalse(boundary.CanApprove);
        Assert.IsFalse(boundary.CanMarkReadyForReview);
        Assert.IsFalse(boundary.CanRequestReviewers);
        Assert.IsFalse(boundary.CanMerge);
        Assert.IsFalse(boundary.CanRelease);
        Assert.IsFalse(boundary.CanDeploy);
        Assert.IsFalse(boundary.CanContinueWorkflow);
    }

    private static void WriteJson<T>(string path, T value) =>
        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions));

    private static T? ReadJson<T>(string path) =>
        File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions) : default;

    private static async Task<(int ExitCode, string Output, string Error)> RunCliAsync(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await IronDevCli.RunAsync(args, output, error, CancellationToken.None).ConfigureAwait(false);
        return (exitCode, output.ToString(), error.ToString());
    }

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "irondev-aq-" + Guid.NewGuid().ToString("N"));
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
            // Best-effort cleanup for Windows file handles.
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

        throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
