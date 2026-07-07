using IronDev.Core.Provisioning;
using IronDev.Data.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.UnitTests.Provisioning;

/// <summary>
/// PROJECT-3: the readiness evaluator is pure and deterministic. These tests pin the
/// contract: detection proposes and still blocks; only stored (human-confirmed) truth
/// unblocks; every blocking check names a remedy; blocked states use the spec vocabulary.
/// </summary>
[TestClass]
public sealed class ProvisioningReadinessEvaluatorTests
{
    private static ProvisioningEvaluationInput FullyConfirmedInput() => new()
    {
        ProjectId = 3,
        RepoPath = @"C:\repos\BookSeller",
        RepoPathExists = true,
        RepoPathIsSafe = true,
        IsGitRepository = true,
        StoredProfile = new ProjectProfile { ProjectId = 3, PrimaryLanguage = "C#", ApplicationType = "WebApi", SolutionFile = "BookSeller.slnx" },
        StoredBuildCommand = new ProjectCommand { ProjectId = 3, CommandType = "Build", CommandText = "dotnet build BookSeller.slnx" },
        StoredTestCommand = new ProjectCommand { ProjectId = 3, CommandType = "Test", CommandText = "dotnet test BookSeller.slnx" }
    };

    [TestMethod]
    public void FullyConfirmedProject_IsReady_WithNoBlockedStates()
    {
        var result = ProvisioningReadinessEvaluator.Evaluate(FullyConfirmedInput());

        Assert.IsTrue(result.IsReady);
        Assert.AreEqual(0, result.BlockedStates.Count);
        Assert.IsNull(result.ProposedProfile, "A stored profile means no proposal is pending.");
        Assert.AreEqual(ProjectProvisioningReadiness.BoundaryText, result.Boundary);
    }

    [TestMethod]
    public void MissingRepoPath_Blocks_WithNamedRemedy()
    {
        var result = ProvisioningReadinessEvaluator.Evaluate(FullyConfirmedInput() with { RepoPath = null, RepoPathExists = false });

        Assert.IsFalse(result.IsReady);
        CollectionAssert.Contains(result.BlockedStates.ToList(), ProvisioningBlockedStates.MissingRepoPath);
        var check = result.Checks.Single(c => c.Name == "Repo path");
        Assert.IsTrue(check.Blocking);
        Assert.IsFalse(string.IsNullOrWhiteSpace(check.Remedy), "A blocking check without a remedy is a dead end.");
    }

    [TestMethod]
    public void ConfiguredPathThatDoesNotExist_Blocks_AsMissingRepoPath()
    {
        var result = ProvisioningReadinessEvaluator.Evaluate(
            FullyConfirmedInput() with { RepoPath = @"C:\nowhere\gone", RepoPathExists = false });

        Assert.IsFalse(result.IsReady);
        CollectionAssert.Contains(result.BlockedStates.ToList(), ProvisioningBlockedStates.MissingRepoPath);
        StringAssert.Contains(result.Checks.Single(c => c.Name == "Repo path").Evidence, @"C:\nowhere\gone");
    }

    [TestMethod]
    public void UnsafeRoot_Blocks_AsUnsafeRepoPath()
    {
        var result = ProvisioningReadinessEvaluator.Evaluate(FullyConfirmedInput() with
        {
            RepoPathIsSafe = false,
            RepoPathSafetyDetail = @"C:\ is a drive root."
        });

        Assert.IsFalse(result.IsReady);
        CollectionAssert.Contains(result.BlockedStates.ToList(), ProvisioningBlockedStates.UnsafeRepoPath);
        var check = result.Checks.Single(c => c.Name == "Repo path safety");
        Assert.AreEqual(ProvisioningCheckStates.Unsafe, check.State);
        StringAssert.Contains(check.Evidence, "drive root");
    }

    [TestMethod]
    public void DetectedButUnconfirmedCommand_StillBlocks_AndCarriesTheCandidate()
    {
        var result = ProvisioningReadinessEvaluator.Evaluate(FullyConfirmedInput() with
        {
            StoredBuildCommand = null,
            DetectedBuildCommand = "dotnet build BookSeller.slnx"
        });

        Assert.IsFalse(result.IsReady, "Detection proposes; only a human confirms.");
        CollectionAssert.Contains(result.BlockedStates.ToList(), ProvisioningBlockedStates.MissingBuildCommand);
        var check = result.Checks.Single(c => c.Name == "Build command");
        Assert.AreEqual(ProvisioningCheckStates.NeedsConfirmation, check.State);
        Assert.AreEqual("dotnet build BookSeller.slnx", check.DetectedValue);
        Assert.IsTrue(check.Blocking);
    }

    [TestMethod]
    public void MissingTestCommandWithNoCandidate_Blocks_AsMissing()
    {
        var result = ProvisioningReadinessEvaluator.Evaluate(FullyConfirmedInput() with { StoredTestCommand = null });

        Assert.IsFalse(result.IsReady);
        CollectionAssert.Contains(result.BlockedStates.ToList(), ProvisioningBlockedStates.MissingTestCommand);
        Assert.AreEqual(ProvisioningCheckStates.Missing, result.Checks.Single(c => c.Name == "Test command").State);
    }

    [TestMethod]
    public void DetectedProfileWithoutStoredProfile_Blocks_AndProposesForConfirmation()
    {
        var detected = new ProjectProfile { ProjectId = 3, PrimaryLanguage = "C#", ApplicationType = "WebApi" };
        var result = ProvisioningReadinessEvaluator.Evaluate(FullyConfirmedInput() with
        {
            StoredProfile = null,
            DetectedProfile = detected
        });

        Assert.IsFalse(result.IsReady);
        CollectionAssert.Contains(result.BlockedStates.ToList(), ProvisioningBlockedStates.UnknownArchitecture);
        Assert.AreSame(detected, result.ProposedProfile, "The proposal rides along for one-click human confirmation.");
        Assert.AreEqual(
            ProvisioningCheckStates.NeedsConfirmation,
            result.Checks.Single(c => c.Name == "Architecture profile").State);
    }

    [TestMethod]
    public void EveryBlockingCheck_NamesARemedy()
    {
        // The everything-is-wrong project: no path, nothing stored, nothing detected.
        var result = ProvisioningReadinessEvaluator.Evaluate(new ProvisioningEvaluationInput { ProjectId = 9 });

        Assert.IsFalse(result.IsReady);
        foreach (var check in result.Checks.Where(c => c.Blocking))
        {
            Assert.IsFalse(
                string.IsNullOrWhiteSpace(check.Remedy),
                $"Blocking check '{check.Name}' has no remedy — a fresh machine must get named remedies.");
        }
    }

    [TestMethod]
    public void DirtyRepoState_IsHonestlyNotEvaluated_AndNeverBlocks()
    {
        var result = ProvisioningReadinessEvaluator.Evaluate(FullyConfirmedInput());

        var check = result.Checks.Single(c => c.Name == "Dirty-repo state");
        Assert.AreEqual(ProvisioningCheckStates.NotEvaluated, check.State);
        Assert.IsFalse(check.Blocking);
    }
}
