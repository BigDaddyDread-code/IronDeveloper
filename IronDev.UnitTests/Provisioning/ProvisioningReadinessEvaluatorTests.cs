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
        WorkTreeState = ProvisioningWorkTreeStates.Clean,
        StoredProfile = new ProjectProfile { ProjectId = 3, PrimaryLanguage = "C#", ApplicationType = "WebApi", SolutionFile = "BookSeller.slnx", AllowBuilderApply = true },
        StoredBuildCommand = new ProjectCommand { ProjectId = 3, CommandType = "Build", CommandText = "dotnet build BookSeller.slnx" },
        StoredTestCommand = new ProjectCommand { ProjectId = 3, CommandType = "Test", CommandText = "dotnet test BookSeller.slnx" },
        // F-E: run-start requirements are provisioning requirements — one readiness truth.
        HasCodeIndex = true,
        IndexingStatus = "Ready"
    };

    [TestMethod]
    public void FullyConfirmedProject_IsReady_WithNoBlockedStates()
    {
        var result = ProvisioningReadinessEvaluator.Evaluate(FullyConfirmedInput());

        Assert.IsTrue(result.IsReady);
        Assert.AreEqual(0, result.BlockedStates.Count);
        Assert.AreEqual(0, result.BlockedCount);
        Assert.AreEqual(ProvisioningActionKinds.OpenBoard, result.NextAction.Kind);
        Assert.IsTrue(result.NextAction.Allowed);
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
        Assert.AreEqual(ProvisioningCheckCodes.BuildCommand, check.Code);
        Assert.AreEqual(ProvisioningActionKinds.ConfirmBuildCommand, result.NextAction.Kind);
        Assert.AreEqual(ProvisioningCheckCodes.BuildCommand, result.NextAction.CheckCode);
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
    public void CleanWorkTree_IsConfirmed_AndDoesNotBlock()
    {
        var result = ProvisioningReadinessEvaluator.Evaluate(FullyConfirmedInput());

        var check = result.Checks.Single(c => c.Name == "Dirty-repo state");
        Assert.AreEqual(ProvisioningCheckStates.Confirmed, check.State);
        Assert.IsFalse(check.Blocking);
        Assert.IsTrue(result.IsReady);
    }

    [TestMethod]
    public void DirtyWorkTree_Blocks_AsBlockedDirtyRepo_WithNamedRemedy()
    {
        var result = ProvisioningReadinessEvaluator.Evaluate(FullyConfirmedInput() with
        {
            WorkTreeState = ProvisioningWorkTreeStates.Dirty,
            WorkTreeDetail = "2 changed path(s): src/App.cs, README.md"
        });

        Assert.IsFalse(result.IsReady, "A governed run must start from an unambiguous source tree.");
        CollectionAssert.Contains(result.BlockedStates.ToList(), ProvisioningBlockedStates.DirtyRepo);
        var check = result.Checks.Single(c => c.Name == "Dirty-repo state");
        Assert.IsTrue(check.Blocking);
        StringAssert.Contains(check.Evidence, "src/App.cs");
        Assert.IsFalse(string.IsNullOrWhiteSpace(check.Remedy), "A blocking check without a remedy is a dead end.");
    }

    [TestMethod]
    public void UnknownWorkTree_IsHonestlyNotEvaluated_AndNeverBlocks()
    {
        var result = ProvisioningReadinessEvaluator.Evaluate(FullyConfirmedInput() with
        {
            WorkTreeState = ProvisioningWorkTreeStates.Unknown,
            WorkTreeDetail = "git status timed out after 15 seconds."
        });

        var check = result.Checks.Single(c => c.Name == "Dirty-repo state");
        Assert.AreEqual(ProvisioningCheckStates.NotEvaluated, check.State);
        Assert.IsFalse(check.Blocking, "An unanswerable git is named, never guessed — and never blocks by itself.");
        StringAssert.Contains(check.Evidence, "timed out");
        Assert.IsTrue(result.IsReady, "Unknown work-tree state alone must not block an otherwise-ready project.");
    }

    [TestMethod]
    public void NotIndexedProject_Blocks_AsBlockedProjectNotIndexed_WithNamedRemedy()
    {
        // DOGFOOD-2 finding F-E: provisioning said ReadyToRun while the run start
        // refused for the missing code index. One readiness truth.
        var result = ProvisioningReadinessEvaluator.Evaluate(FullyConfirmedInput() with { HasCodeIndex = false, IndexingStatus = null });

        Assert.IsFalse(result.IsReady, "The Builder's readiness gate refuses unindexed projects — provisioning must say so first.");
        CollectionAssert.Contains(result.BlockedStates.ToList(), ProvisioningBlockedStates.ProjectNotIndexed);
        var check = result.Checks.Single(c => c.Name == "Code index");
        Assert.IsTrue(check.Blocking);
        StringAssert.Contains(check.Remedy, "Index project");
        Assert.AreEqual(ProvisioningActionKinds.IndexProject, check.ActionKind);
    }

    [TestMethod]
    public void IndexNotReady_Blocks_WithTheStatusNamed()
    {
        var result = ProvisioningReadinessEvaluator.Evaluate(FullyConfirmedInput() with { IndexingStatus = "Stale Index" });

        Assert.IsFalse(result.IsReady);
        CollectionAssert.Contains(result.BlockedStates.ToList(), ProvisioningBlockedStates.ProjectNotIndexed);
        StringAssert.Contains(result.Checks.Single(c => c.Name == "Code index").Evidence, "Stale Index");
    }

    [TestMethod]
    public void BuilderApplyDisabled_Blocks_AndTheRemedyIsADeliberateHumanAct()
    {
        // F-E: AllowBuilderApply=false is a valid, safe default — but the run start
        // refuses while it is off, so readiness names it instead of letting the
        // run's refusal be the first mention. The remedy states the boundary:
        // enabling it permits governed workspace writes only.
        var input = FullyConfirmedInput();
        input.StoredProfile!.AllowBuilderApply = false;
        var result = ProvisioningReadinessEvaluator.Evaluate(input);

        Assert.IsFalse(result.IsReady);
        CollectionAssert.Contains(result.BlockedStates.ToList(), ProvisioningBlockedStates.BuilderApplyDisabled);
        var check = result.Checks.Single(c => c.Name == "Builder apply permission");
        Assert.IsTrue(check.Blocking);
        StringAssert.Contains(check.Remedy, "governed Builder workspace writes");
        StringAssert.Contains(check.Remedy, "does not approve");
        Assert.AreEqual(ProvisioningActionKinds.EnableBuilderApply, check.ActionKind);
    }

    [TestMethod]
    public void NonGitRepository_GetsNoWorkTreeCheck_GitCheckAlreadyCoversIt()
    {
        var result = ProvisioningReadinessEvaluator.Evaluate(FullyConfirmedInput() with
        {
            IsGitRepository = false,
            WorkTreeState = ProvisioningWorkTreeStates.NotAGitRepository
        });

        Assert.IsFalse(result.Checks.Any(c => c.Name == "Dirty-repo state"));
        Assert.AreEqual(
            ProvisioningCheckStates.NeedsConfirmation,
            result.Checks.Single(c => c.Name == "Git repository").State);
    }

    [TestMethod]
    public void EveryReadinessCheck_HasAStableCode()
    {
        var result = ProvisioningReadinessEvaluator.Evaluate(new ProvisioningEvaluationInput { ProjectId = 9 });

        Assert.IsTrue(result.Checks.Count > 0);
        Assert.IsFalse(result.Checks.Any(check => string.IsNullOrWhiteSpace(check.Code)));
        Assert.IsFalse(result.Checks.Any(check => check.Code == ProvisioningCheckCodes.Unknown));
    }

    [TestMethod]
    public void NextAction_UsesStableCode_NotDisplayLabel()
    {
        var result = ProvisioningReadinessEvaluator.Evaluate(FullyConfirmedInput() with
        {
            StoredTestCommand = null,
            DetectedTestCommand = "dotnet test BookSeller.slnx"
        });

        var check = result.Checks.Single(candidate => candidate.Code == ProvisioningCheckCodes.TestCommand);
        Assert.AreEqual("Test command", check.Label);
        Assert.AreEqual(ProvisioningActionKinds.ConfirmTestCommand, result.NextAction.Kind);
        Assert.AreEqual(ProvisioningCheckCodes.TestCommand, result.NextAction.CheckCode);
        Assert.AreEqual(ProvisioningBlockedStates.MissingTestCommand, result.NextAction.ReasonCode);
    }

    [TestMethod]
    public void MissingCodeIndex_UsesSemanticIndexActionWithoutRawApiRecipe()
    {
        var result = ProvisioningReadinessEvaluator.Evaluate(FullyConfirmedInput() with
        {
            HasCodeIndex = false,
            IndexingStatus = null
        });

        var check = result.Checks.Single(candidate => candidate.Code == ProvisioningCheckCodes.CodeIndex);
        Assert.AreEqual(ProvisioningActionKinds.IndexProject, check.ActionKind);
        Assert.AreEqual(ProvisioningActionKinds.IndexProject, result.NextAction.Kind);
        Assert.IsFalse(check.Remedy.Contains("POST /api/", StringComparison.Ordinal));
    }

    [TestMethod]
    public void DisabledBuilderApply_UsesSemanticPermissionActionWithoutProfileRoundTripRecipe()
    {
        var input = FullyConfirmedInput();
        input.StoredProfile!.AllowBuilderApply = false;

        var result = ProvisioningReadinessEvaluator.Evaluate(input);

        var check = result.Checks.Single(candidate => candidate.Code == ProvisioningCheckCodes.BuilderApplyPermission);
        Assert.AreEqual(ProvisioningActionKinds.EnableBuilderApply, check.ActionKind);
        Assert.AreEqual(ProvisioningActionKinds.EnableBuilderApply, result.NextAction.Kind);
        Assert.IsFalse(check.Remedy.Contains("GET /api/", StringComparison.Ordinal));
        Assert.IsFalse(check.Remedy.Contains("POST", StringComparison.Ordinal));
    }

    [TestMethod]
    public void UnknownFutureCheckCode_RemainsSerializableContractData()
    {
        var check = new ProvisioningCheck
        {
            Code = "FutureRepositoryPolicy",
            Name = "Future repository policy",
            State = ProvisioningCheckStates.NeedsConfirmation,
            Evidence = "A future backend check requires attention.",
            Remedy = "Follow the backend-provided remedy.",
            Blocking = true
        };

        Assert.AreEqual("FutureRepositoryPolicy", check.Code);
        Assert.AreEqual(ProvisioningActionKinds.ResolveAdditionalSetup, check.ActionKind);
    }
}
