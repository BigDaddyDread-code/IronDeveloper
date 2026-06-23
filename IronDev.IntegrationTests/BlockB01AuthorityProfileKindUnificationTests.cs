using System.Reflection;
using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockB01AuthorityProfileKindUnificationTests
{
    private static readonly RunAuthorityOperationKind[] ExpectedProposalOnlyAllowedOperations =
    [
        RunAuthorityOperationKind.RepoInspect,
        RunAuthorityOperationKind.TaskInterpretation,
        RunAuthorityOperationKind.DisposableWorkspaceCreate,
        RunAuthorityOperationKind.DisposableWorkspaceModify,
        RunAuthorityOperationKind.DisposableWorkspaceValidate,
        RunAuthorityOperationKind.PatchProposal,
        RunAuthorityOperationKind.PatchPackageWrite,
        RunAuthorityOperationKind.ValidationResultPackageWrite,
        RunAuthorityOperationKind.GovernedStatusInspect
    ];

    private static readonly RunAuthorityOperationKind[] ExpectedProposalOnlyForbiddenOperations =
    [
        RunAuthorityOperationKind.SourceApply,
        RunAuthorityOperationKind.Rollback,
        RunAuthorityOperationKind.Commit,
        RunAuthorityOperationKind.Push,
        RunAuthorityOperationKind.DraftPullRequest,
        RunAuthorityOperationKind.ReadyForReview,
        RunAuthorityOperationKind.Merge,
        RunAuthorityOperationKind.Release,
        RunAuthorityOperationKind.Deployment,
        RunAuthorityOperationKind.MemoryPromotion,
        RunAuthorityOperationKind.WorkflowContinuation,
        RunAuthorityOperationKind.ApprovalRequestCreate,
        RunAuthorityOperationKind.PolicySatisfaction,
        RunAuthorityOperationKind.ProviderMutation,
        RunAuthorityOperationKind.PackagePublication,
        RunAuthorityOperationKind.DurableSourceMutation,
        RunAuthorityOperationKind.DurableEventWrite
    ];

    [TestMethod]
    public void BlockB01_RunProfileContractsUseCanonicalAuthorityProfileKind()
    {
        var coreTypes = typeof(RunAuthorityProfile).Assembly.GetTypes();

        Assert.IsFalse(coreTypes.Any(type => type.Name == "RunAuthorityProfileKind"));
        Assert.AreEqual(typeof(AuthorityProfileKind), PropertyType<RunAuthorityProfile>(nameof(RunAuthorityProfile.Kind)));
        Assert.AreEqual(typeof(AuthorityProfileKind), PropertyType<RunAuthorityDecision>(nameof(RunAuthorityDecision.ProfileKind)));
        Assert.AreEqual(typeof(AuthorityProfileKind), PropertyType<AuthorityProfileStatusRequest>(nameof(AuthorityProfileStatusRequest.ProfileKind)));
    }

    [TestMethod]
    public void BlockB01_AuthorityProfileKindNumericValuesRemainStable()
    {
        Assert.AreEqual(0, (int)AuthorityProfileKind.Unknown);
        Assert.AreEqual(1, (int)AuthorityProfileKind.ProposalOnly);
        Assert.AreEqual(2, (int)AuthorityProfileKind.AskBeforeMutation);
        Assert.AreEqual(3, (int)AuthorityProfileKind.BoundedRunAuthority);
    }

    [TestMethod]
    public void BlockB01_ProposalOnlyProfileStillValidatesWithExistingCeiling()
    {
        var profile = ProposalOnlyProfile();
        var validation = RunAuthorityProfileValidator.Validate(profile);

        Assert.IsTrue(validation.IsValid, string.Join(", ", validation.Issues));
        Assert.AreEqual(AuthorityProfileKind.ProposalOnly, profile.Kind);
        CollectionAssert.AreEquivalent(ExpectedProposalOnlyAllowedOperations, profile.AllowedOperations.ToArray());
        CollectionAssert.AreEquivalent(ExpectedProposalOnlyForbiddenOperations, profile.ForbiddenOperations.ToArray());
        CollectionAssert.AreEquivalent(
            ExpectedProposalOnlyAllowedOperations,
            RunAuthorityProfileValidator.ProposalOnlyAllowedOperations.ToArray());
        CollectionAssert.AreEquivalent(
            ExpectedProposalOnlyForbiddenOperations,
            RunAuthorityProfileValidator.ProposalOnlyForbiddenOperations.ToArray());
    }

    [TestMethod]
    public void BlockB01_ProposalOnlyAllowanceRemainsNecessaryButNotSufficient()
    {
        var decision = RunAuthorityProfileEvaluator.Evaluate(
            ProposalOnlyProfile(),
            RunAuthorityOperationKind.PatchPackageWrite);

        Assert.IsTrue(decision.IsAllowedByProfile, string.Join(", ", decision.BlockedReasons));
        Assert.AreEqual(AuthorityProfileKind.ProposalOnly, decision.ProfileKind);
        AssertContains(decision.RequiredIndependentChecks, "operation-specific validation still required");
        AssertContains(decision.RequiredIndependentChecks, "profile allowance is necessary but not sufficient");
        AssertContains(decision.ForbiddenActions, "do not treat profile allowance as approval");
        AssertContains(decision.ForbiddenActions, "do not treat profile allowance as policy satisfaction");
        AssertContains(decision.ForbiddenActions, "do not treat profile allowance as execution authority");
        AssertContains(decision.ForbiddenActions, "do not mutate durable source from profile allowance");
    }

    [TestMethod]
    public void BlockB01_ProposalOnlyForbiddenOperationsRemainBlocked()
    {
        foreach (var operation in ExpectedProposalOnlyForbiddenOperations)
        {
            var decision = RunAuthorityProfileEvaluator.Evaluate(ProposalOnlyProfile(), operation);

            Assert.IsFalse(decision.IsAllowedByProfile, operation.ToString());
            Assert.AreEqual(AuthorityProfileKind.ProposalOnly, decision.ProfileKind);
            AssertContains(decision.BlockedReasons, $"ProposalOnly does not allow {operation}.");
            AssertContains(decision.ForbiddenActions, $"do not perform {operation} under ProposalOnly");
            AssertContains(decision.RequiredIndependentChecks, "explicit governed authority required outside this profile");
        }
    }

    [TestMethod]
    public void BlockB01_ProposalOnlySafeAndDangerousFlagsRemainStrict()
    {
        AssertInvalid(
            ProposalOnlyProfile() with { CanReadRepo = false },
            "ProposalOnlySafeFlagMustBeTrue:CanReadRepo");
        AssertInvalid(
            ProposalOnlyProfile() with { CanMutateDisposableWorkspace = false },
            "ProposalOnlySafeFlagMustBeTrue:CanMutateDisposableWorkspace");
        AssertInvalid(
            ProposalOnlyProfile() with { CanWriteProposalEvidence = false },
            "ProposalOnlySafeFlagMustBeTrue:CanWriteProposalEvidence");
        AssertInvalid(
            ProposalOnlyProfile() with { CanInspectGovernedStatus = false },
            "ProposalOnlySafeFlagMustBeTrue:CanInspectGovernedStatus");

        AssertInvalid(
            ProposalOnlyProfile() with { CanMutateDurableSource = true },
            "ProposalOnlyDangerousFlagMustBeFalse:CanMutateDurableSource");
        AssertInvalid(
            ProposalOnlyProfile() with { CanApplyPatch = true },
            "ProposalOnlyDangerousFlagMustBeFalse:CanApplyPatch");
        AssertInvalid(
            ProposalOnlyProfile() with { CanCommit = true },
            "ProposalOnlyDangerousFlagMustBeFalse:CanCommit");
        AssertInvalid(
            ProposalOnlyProfile() with { CanPush = true },
            "ProposalOnlyDangerousFlagMustBeFalse:CanPush");
        AssertInvalid(
            ProposalOnlyProfile() with { CanContinueWorkflow = true },
            "ProposalOnlyDangerousFlagMustBeFalse:CanContinueWorkflow");
    }

    [TestMethod]
    public void BlockB01_UnknownAuthorityProfileKindFailsClosed()
    {
        var profile = ProposalOnlyProfile() with { Kind = AuthorityProfileKind.Unknown };
        var validation = RunAuthorityProfileValidator.Validate(profile);
        var decision = RunAuthorityProfileEvaluator.Evaluate(profile, RunAuthorityOperationKind.PatchPackageWrite);

        Assert.IsFalse(validation.IsValid);
        AssertContains(validation.Issues, "AuthorityProfileKindKnownRequired");
        Assert.IsFalse(decision.IsAllowedByProfile);
        Assert.AreEqual(AuthorityProfileKind.Unknown, decision.ProfileKind);
        AssertContains(decision.BlockedReasons, "RunAuthorityProfileInvalid");
        AssertContains(decision.BlockedReasons, "RunAuthorityProfileInvalid:AuthorityProfileKindKnownRequired");
    }

    [TestMethod]
    public void BlockB01_KnownUnsupportedAuthorityProfileKindsFailClosed()
    {
        foreach (var kind in new[] { AuthorityProfileKind.AskBeforeMutation, AuthorityProfileKind.BoundedRunAuthority })
        {
            var profile = ProposalOnlyProfile() with { Kind = kind };
            var validation = RunAuthorityProfileValidator.Validate(profile);
            var decision = RunAuthorityProfileEvaluator.Evaluate(profile, RunAuthorityOperationKind.PatchPackageWrite);

            Assert.IsFalse(validation.IsValid, kind.ToString());
            AssertContains(validation.Issues, $"AuthorityProfileKindUnsupported:{kind}");
            Assert.IsFalse(decision.IsAllowedByProfile, kind.ToString());
            Assert.AreEqual(kind, decision.ProfileKind);
            AssertContains(decision.BlockedReasons, "RunAuthorityProfileInvalid");
            AssertContains(decision.BlockedReasons, $"RunAuthorityProfileInvalid:AuthorityProfileKindUnsupported:{kind}");
            AssertContains(decision.ForbiddenActions, "do not proceed from invalid run authority profile");
        }
    }

    [TestMethod]
    public void BlockB01_StaticContract_HasNoDuplicateEnumOrCompatibilityMapper()
    {
        var root = FindRepositoryRoot();
        var governanceFiles = Directory.GetFiles(
            Path.Combine(root, "IronDev.Core", "Governance"),
            "*.cs",
            SearchOption.AllDirectories);
        var text = string.Join(Environment.NewLine, governanceFiles.Select(File.ReadAllText));

        Assert.IsFalse(text.Contains("RunAuthorityProfileKind", StringComparison.Ordinal), "RunAuthorityProfileKind remains in Core/Governance.");
        Assert.IsFalse(text.Contains("RunAuthorityProfileKindMapper", StringComparison.Ordinal), "Compatibility mapper must not exist.");
    }

    [TestMethod]
    public void BlockB01_StaticContract_RunProfileFilesDoNotAddMutationSurfaces()
    {
        var root = FindRepositoryRoot();
        var files = Directory.GetFiles(
            Path.Combine(root, "IronDev.Core", "Governance", "RunProfiles"),
            "*.cs",
            SearchOption.TopDirectoryOnly);
        var text = string.Join(Environment.NewLine, files.Select(File.ReadAllText));
        var forbidden = new[]
        {
            "File.Write",
            "Directory.CreateDirectory",
            "Process.Start",
            "ProcessStartInfo",
            "HttpClient",
            "IGovernanceEventStore",
            "ISourceApplyExecutor",
            "RollbackExecutor",
            "CommitExecutor",
            "PushExecutor",
            "PullRequestExecutor",
            "MergeExecutor",
            "ReleaseExecutor",
            "DeploymentExecutor",
            "CreateApproval(",
            "SatisfyPolicy(",
            "PromoteMemory(",
            "ContinueWorkflow("
        };

        foreach (var marker in forbidden)
            Assert.IsFalse(text.Contains(marker, StringComparison.OrdinalIgnoreCase), marker);
    }

    [TestMethod]
    public void BlockB01_ReceiptRecordsBoundary()
    {
        var doc = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Docs", "receipts", "B01_AUTHORITY_PROFILE_KIND_UNIFICATION.md"));

        StringAssert.Contains(doc, "AuthorityProfileKind is canonical.");
        StringAssert.Contains(doc, "RunAuthorityProfileKind was removed.");
        StringAssert.Contains(doc, "ProposalOnly behavior did not widen.");
        StringAssert.Contains(doc, "AskBeforeMutation and BoundedRunAuthority did not become runnable run profiles in this PR.");
        StringAssert.Contains(doc, "This PR adds no executor, mutation, source apply, rollback, commit, push, PR, merge, release, deploy, memory promotion, or workflow continuation path.");
        StringAssert.Contains(doc, "One authority vocabulary, one interpretation path.");
    }

    private static Type PropertyType<T>(string name) =>
        typeof(T).GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.PropertyType
        ?? throw new MissingMemberException(typeof(T).FullName, name);

    private static RunAuthorityProfile ProposalOnlyProfile() =>
        new()
        {
            ProfileId = "proposal-only",
            Kind = AuthorityProfileKind.ProposalOnly,
            AllowedOperations = ExpectedProposalOnlyAllowedOperations,
            ForbiddenOperations = ExpectedProposalOnlyForbiddenOperations,
            CanReadRepo = true,
            CanMutateDisposableWorkspace = true,
            CanWriteProposalEvidence = true,
            CanInspectGovernedStatus = true,
            CanMutateDurableSource = false,
            CanApplyPatch = false,
            CanExecuteRollback = false,
            CanCommit = false,
            CanPush = false,
            CanCreatePullRequest = false,
            CanMarkReadyForReview = false,
            CanMerge = false,
            CanRelease = false,
            CanDeploy = false,
            CanCreateApprovalRequest = false,
            CanSatisfyPolicy = false,
            CanPromoteMemory = false,
            CanContinueWorkflow = false,
            CanExecuteProviderMutation = false,
            CanPublishPackage = false
        };

    private static void AssertInvalid(RunAuthorityProfile profile, string expectedIssue)
    {
        var validation = RunAuthorityProfileValidator.Validate(profile);

        Assert.IsFalse(validation.IsValid);
        AssertContains(validation.Issues, expectedIssue);
    }

    private static void AssertContains(IEnumerable<string> values, string expected)
    {
        Assert.IsTrue(
            values.Any(value => string.Equals(value, expected, StringComparison.OrdinalIgnoreCase)),
            $"Expected '{expected}' in: {string.Join(", ", values)}");
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
